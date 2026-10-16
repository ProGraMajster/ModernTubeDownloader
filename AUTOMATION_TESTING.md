# UI automation testing

ModernTubeDownloader uses the existing ModernFormsNext `AccessibleObject` tree through `ModernFormsNext.Automation` and its Windows named-pipe bridge. It does not maintain a second UI tree. The bridge is **off by default** and is compiled only into Debug builds. A normal `ModernTubeDownloader.exe` launch has no Automation endpoint; a Release build does not contain the Automation project references and rejects `--automation`.

## Start and discover

Candidate.3 added `NotificationsSection`, `NotificationEnabled`, all individual `Notification<Property>` checkboxes, `NotificationSound`, `NotificationPresentation`, `NotificationDiagnostics`, `NotificationsTest` and `NotificationsReset`. Native smoke: `scripts/Test-Automation.ps1 -NotificationsSmoke -CaptureVisual -Language pl -Theme Dark`. Add `-NotificationManualHoldSeconds 180` only when a human is ready to observe/click the real OS notification. The test uses an isolated data root and fake media tools; OS provider/submission is real, but fake media is not a real download qualification. Close that owned QA process before rebuild or another smoke. Candidate 1.2 Settings has eleven categories; the original scroll regression uses DEBUG-only all-cards mode and checked 18 cards. The 1.3 candidate adds the interface card, so its current all-cards check requires exactly 19. The old 14-card count is historical.

The UI harness captures only its owned HWND. Native banner/center/cold-click evidence is separate; see [candidate report](CANDIDATE3_NOTIFICATIONS_PIPELINE_REPORT.md). Running with an isolated environment-variable data root does not propagate that root to an Explorer/COM cold launch: do not claim an isolated cold-start test unless profile/identity ownership is established.

Build the Debug application and the CLI from the same pinned ModernFormsNext source checkout (`d36015711f63945a49025675acb9aca3df60101d`, origin/master fetched 2026-10-11):

```powershell
dotnet build .\ModernTubeDownloader\ModernTubeDownloader.csproj -c Debug -m:1 /p:UseSharedCompilation=false
dotnet build .\.mfn-master-worktree\ModernFormsNext.Automation.Cli\ModernFormsNext.Automation.Cli.csproj -c Debug -m:1 /p:UseSharedCompilation=false
.\ModernTubeDownloader\bin\Debug\net10.0-windows10.0.26100.0\ModernTubeDownloader.exe --automation
```

In a second terminal:

```powershell
$cli = '.\.mfn-master-worktree\ModernFormsNext.Automation.Cli\bin\Debug\net10.0-windows\ModernFormsNext.Automation.Cli.dll'
dotnet $cli list --json
dotnet $cli roots --pid <PID> --json
dotnet $cli tree --pid <PID> --depth 8
dotnet $cli find --pid <PID> --automation-id DownloadUrlInput --json
```

Use the returned `handle.sessionId` and `handle.runtimeId` with `inspect` and `action`:

```powershell
dotnet $cli inspect --pid <PID> --session <SESSION> --node <NODE> --json
dotnet $cli action --pid <PID> --session <SESSION> --node <NODE> --action Invoke --json
dotnet $cli wait --pid <PID> --condition Enabled --automation-id AddToQueueButton --timeout-ms 30000 --json
```

For `SetValue`, supply the value through `--value-stdin`; do not pass URLs or potentially sensitive input on the command line. CLI `wait` is preferred to a fixed sleep. When Details is open, pass `--root <ROOT_ID>` because the process has two registered roots. The Windows bridge uses the current user's local named-pipe discovery and authentication policy; it is intended only for consciously enabled development and test sessions, not as a remote-control service or security boundary between different elevation levels.

## Stable IDs

| Area | Automation IDs |
| --- | --- |
| Main window and navigation | `MainWindow`, `NavDownloads`, `NavLive`, `NavHistory`, `NavOther`, `NavSettings` |
| Settings categories | `SettingsCategoryGeneral`, `SettingsCategoryDownloads`, `SettingsCategoryLive`, `SettingsCategoryNotifications`, `SettingsCategoryAuthentication`, `SettingsCategoryBrowser`, `SettingsCategoryWebRemote`, `SettingsCategoryUpdates`, `SettingsCategoryStartup`, `SettingsCategoryPersonalization`, `SettingsCategoryAdvanced` |
| Tray/startup | `TrayEnabled`, `TrayMinimizeBehavior`, `TrayCloseBehavior`, `WindowsAutostartEnabled`, `WindowsAutostartHidden`, `StartupApply` |
| App updater | `ApplicationUpdatesSection`, `ApplicationUpdateCheck`, `ApplicationUpdateStatus`, `ApplicationUpdateFrequency`, `ApplicationUpdateMode`, `ApplicationUpdateDownload`, `ApplicationUpdateInstall` |
| Typography/profile | `AppearanceTextPreset`, `AppearanceTextScale`, `AppearanceTextReset`, `AppearanceSection`, `AppearancePresetDefault`, `AppearancePresetCompact`, `AppearancePresetLarge`, `AppearancePresetMinimal`, `AppearanceColorAccent` |
| Batch | `BatchWindow`, `BatchFilePath`, `BatchSearch`, `BatchQuality`, `BatchContainer`, `BatchWorkers`, `BatchFilter`, `BatchRows`, `BatchStatus`, `BatchSelect<N>`, `BatchEntryTitle<N>`, `BatchEntryStatus<N>` |
| LIVE | `LiveUrlInput`, `LiveAnalyzeButton`, `LiveStartButton`, `LiveQualitySelector`, `LiveContainerSelector`, `LiveFromNowOption`, `LiveFromStartOption`, `LiveActiveList`, `LiveScheduledList`, `LivePartialList`, `LiveStatistics`, per-session `LiveStopSaveButton-<guid>`, `LiveCancelButton-<guid>`, `LiveResumeButton-<guid>` |
| Analysis | `DownloadUrlInput`, `PasteButton`, `AnalyzeButton`, `PreviewCard`, `QualitySelector`, `VideoRangeMode`, `VideoRangeStart`, `VideoRangeEnd`, `AddToQueueButton`, `DetailsButton` |
| Playlist preview | `PlaylistPreview`, `PlaylistTitle`, `PlaylistEntriesList`, `PlaylistEntryToggle<N>`, `PlaylistSelectedCount`, `PlaylistQualitySelector`, `PlaylistRangeMode`, `PlaylistRangeStart`, `PlaylistRangeEnd`, `PlaylistSelectAllButton`, `PlaylistClearSelectionButton`, `PlaylistPreviousPageButton`, `PlaylistNextPageButton`, `PlaylistPageCount`, `PlaylistAddSelectedButton`, `PlaylistAddResult` |
| Queue | `QueueList`, `QueuePauseButton`, `QueueMenuButton`, `QueueItem-<guid>`, `QueuePosition-<guid>` |
| Settings | `SettingsScroll`, `SettingsSaveButton`, `LanguageSelector`, `ThemeSelector`, `ConcurrencySelector`, `ShowQueueNumbersToggle`, `WebRemoteEnable`, `WebRemotePort`, `WebRemoteAddress`, `WebRemotePairingCode`, `WebRemoteCopyCode`, `WebRemoteNewCode`, `WebRemoteCodeExpiry`, `WebRemoteUseHttps`, `WebRemoteGenerateCertificate`, `WebRemoteExportCertificate`, `WebRemoteOpen`, `WebRemoteQr` |
| History | `HistorySearch`, `HistoryList` |
| Details | `DetailsWindow`, `DetailsTabs`, `DetailsOverviewTab`, `DetailsMetadataTab`, `DetailsFormatsTab`, `DetailsSubtitlesTab`, `DetailsChaptersTab` |
| Supported sources | `SupportedSourcesWindow`, `SupportedSourcesVersion`, `SupportedSourcesSearch`, `SupportedSourcesTechnicalToggle`, `SupportedSourcesList`, `SupportedSourcesPrevious`, `SupportedSourcesNext`, `SupportedSourcesUrlInput`, `SupportedSourcesCheckButton`, `SupportedSourcesResult`, `SupportedSourcesOpenButton`, `SupportedSourcesCloseButton` |

The Details `*Tab` IDs belong to ModernFormsNext's logical `TabItem` peers. Selecting one exposes the corresponding `Details*Content` page. IDs are independent of PL/EN text and screen geometry. Hidden views and inactive tab pages are not necessarily exposed until selected.

## Smoke and headless tests

Version 1.2.0 adds these serial native smokes:

```powershell
.\scripts\Test-Automation.ps1 -TraySmoke -Language pl -Theme Dark
.\scripts\Test-Automation.ps1 -UpdateSmoke -Language en -Theme Light
.\scripts\Test-Automation.ps1 -BatchSmoke -Language pl -Theme Dark -AppTextScale 200 -CaptureVisual
.\scripts\Test-Automation.ps1 -AppearanceSmoke -Language en -Theme Light -WindowWidth 1400 -WindowHeight 950
.\scripts\Test-Automation.ps1 -SettingsScrollRepro -Language en -Theme Light
.\scripts\Test-Automation.ps1 -PublishStartupSmoke -ApplicationPath .\artifacts\release\win-x64\ModernTubeDownloader.exe
.\scripts\Test-Automation.ps1 -PublishStartupSmoke -PublishedUiSmoke -PublishedNoUpdate -ApplicationPath .\artifacts\release\win-x64\ModernTubeDownloader.exe
```

`-TraySmoke` tests hidden startup/secondary-launch restore/NotifyIcon lifetime without
changing the user's HKCU Run value; it is not shell pointer/login evidence.
`-AppearanceSmoke` applies 125/150/175/200/100% on all pages, reopens Details and changes
presets/accent. Native captures expose MFN composite-font limitations (PERSONALIZATION.md);
passing actions alone are not complete visual/global-font qualification.
Appearance capture allocates its bitmap using the same owned HWND measured by GetWindowRect
as PrintWindow, not potentially clipped semantic-peer bounds. A queued ordinary smoke must
observe and explicitly accept the active-work ShutdownChoiceWindow before asserting clean
shutdown; it never disables the production confirmation to make cleanup pass.
The scheduled-LIVE smoke also requires its active-work shutdown confirmation and
explicitly chooses Exit; completed LIVE can close without that pending-work dialog.
`-RealBatchSmoke` checks public metadata, not media downloads; 20 alias rows do not
mean 20 distinct successful sources. `-PublishStartupSmoke` uses isolated custom QA
tools, not first-run real provisioning. Historical evidence: candidate implementation report.
Final local runs are recorded in FINAL_RELEASE_1_2_0.md. MFN #204/#205 are accepted
post-1.2 typography limitations, not failures of the tested navigation/lifecycle flows.

`-PublishedUiSmoke` launches an external Windows PowerShell UI Automation client
(`scripts/Test-PublishedUI.ps1`) against only the owned production HWND/PID. It invokes
Downloads/LIVE/Other, opens/closes Batch, checks Updates/Startup/Personalization and
persists/resets text scale. `-PublishedNoUpdate` additionally requires the stable
installed version to report Current with no offered/staged update or install intent.
Release has no Debug named-pipe endpoint; neither coordinates nor screenshots drive
these actions. The harness restores any temporary owned URI association to its prior
values, does not change HKCU Run and retains the isolated profile/logs as local evidence.
This is published-binary UI smoke, not real provisioning, login or shell-menu evidence.

Quality-dropdown diagnostics now write `quality-dropdown-native-trace.jsonl` in the isolated profile: iteration/timestamp, fresh canonical handle/states/actions, action result, foreground/active/focus/mouse-capture HWND and visible owned popup HWND/dimensions. Assertions are unchanged and the capture no longer inserts an arbitrary delay. Accepted Expand is an action result, not a promise that real outside clicks/window deactivation cannot close the popup before a later request. Preserve such failures and native evidence; do not retry Expand, reset focus, weaken assertions or mislabel correct Collapsed state as a stale peer. Native ComboBox is windowless; its popup/Form, not the control, owns HWNDs. The current minimal native 100-cycle variants and environmental-interruption trace are documented in STABILIZATION_REPORT.md.

Run `scripts\Test-Automation.ps1` after building the Debug app, the FakeTool test binary, and the CLI. The script creates an isolated temporary data root and synthetic tool executables. It verifies a normal launch without a bridge, then performs `find → inspect → action → wait → inspect` through the CLI for analysis, quality, queue, Settings and all Details tabs. It closes the dialog and app and verifies that discovery is removed. No real media is downloaded and no screenshot or coordinate click is used. The script reports its isolated profile path so logs can be inspected after a failure.

`-WebRemotePairingSmoke` creates an isolated Remote-OFF/auth-ON profile, enables/saves through native Settings, checks safe auth-mode and anonymous 401 for status/queue/live, and confirms an available code without printing it. `-RemoteManualHoldSeconds 180` optionally keeps that test-only listener alive for bounded Chrome pairing QA; it then closes normally. `-SettingsScrollRepro` checks exact card geometry and scrolling across 20 pairing-checkbox toggles; no app data, firewall or certificate-trust configuration is changed.

Optional `-CaptureVisual` inspects owned HWND captures without desktop-coordinate input. `-QualityDropdownSmoke -QualityDropdownPasses 20` checks fresh Expanded state and a supported Collapse action before each collapse, then verifies Collapsed. With capture enabled, the image comes from the same-process popup HWND (not the main window). Unsupported actions fail the test; they are not ignored or converted into success. Earlier PL/Light 1280×850 and EN/Dark 1000×720 runs passed 20 cycles on `bcf2bcb...`; the older pass-15 PL/Light failure lacked native trace and remains historically uncertain. The current minimal/production native 100-cycle variants pass with native/peer agreement. Three exact-CLI interruptions and the final two-cycle PL/Light FAIL are retained: real outside input/deactivation closes the popup before inspect, and the peer correctly reports Collapsed. A controlled owned-window witness confirms this mechanism. No confirmed framework/app bug or peer-accuracy limitation was found; no workaround or weakened assertion was added. Separate ordinary final EN/Dark smoke passes and does not erase the popup diagnostic failures. Detailed counts/profiles are in STABILIZATION_REPORT.md; full focus/DPI qualification is not claimed.

For playlists, run `scripts\Test-Automation.ps1 -PlaylistSmoke`. It analyzes a deterministic 12-entry fixture, excludes one unavailable item and one unchecked item, selects a quality, adds 10 queue jobs, checks duplicate skipping, playlist order, and semantic queue-number badges. `-PlaylistSmoke -PreviewOnly -PlaylistCount 220` checks native pagination while rendering at most 40 rows per page. `-PlaylistSmoke -PlaylistCount 220` exercises bulk insertion and persistence of 218 ordered queue items; for large queues the smoke queries specific automation IDs instead of requesting the CLI's bounded entire-tree response.

The `AutomationTestHostTests` use `ModernFormsNext.Testing` to create Details with full/missing metadata and a QueueItemCard without a native window. Six additional PL/EN × Light/Dark/System cases construct DownloadsView against isolated FakeTool AppServices and verify compact action labels, shared bindings and normal/hover/focus/pressed text contrast. They do not test the whole Downloads flow headlessly. SettingsView/HistoryView and broader service-backed behavior still use the isolated native CLI smoke. TestHost must be disposed synchronously on its creating thread before awaiting service shutdown. Native window startup, named-pipe security/lifetime, operating-system focus and actual rendering remain native smoke responsibilities. Screenshots are useful for visual layout, theme, DPI and clipping; they are not required for the basic functional path.

`scripts\Test-Automation.ps1 -LiveSmoke -SampleUrl https://example.test/live/active` invokes NavLive, analyzes a synthetic active stream, starts it explicitly, inspects its own statistics and Details, and invokes Stop and save. Use `-SampleUrl https://example.test/live/upcoming` for the scheduled list and separate persistence. `-Language pl|en`, `-Theme Light|Dark` and window size parameters exercise alternate presentation. Run native app smokes serially: the real single-instance transport intentionally forwards secondary launches.

For bounded real qualification, `-RealLiveSmoke` uses the Debug semantic UI. `scripts\Test-ReleaseLive.ps1` starts the self-contained published app with an isolated profile, observes the separate `/api/live` API, and verifies Stop and save, History, ffprobe A/V and a short decode. `-ParallelDownloads` adds three short public Blender VOD ranges through the production remote allowlist and asserts three ordinary active jobs while LIVE continues. `-RestartDuringCapture` checks manual resume; add `-AutoResumeAfterRestart` for the opt-in policy. `-InterruptDownloader` kills only the isolated yt-dlp capture child and checks retry into Part 2; add `-NoRetryAfterInterruption` for terminal Partial. Explicit validated local tool paths are required. These are not human A/V playback, browser-extension or physical LAN tests. Current results are in [VALIDATION_REPORT.md](VALIDATION_REPORT.md).

## Modern Dashboard candidate smoke

Use the central MFN pin from Directory.Build.props (the historical pin in the
original commands above is not the current authority). Build Debug, FakeTool and
the matching Automation CLI before running these isolated native checks:

```powershell
.\scripts\Test-Automation.ps1 -DashboardSmoke -CaptureVisual -Language pl -Theme Dark -WindowWidth 1536 -WindowHeight 1024
.\scripts\Test-Automation.ps1 -DashboardSmoke -DashboardSwitchStress -Language en -Theme Dark -WindowWidth 1920 -WindowHeight 1080
.\scripts\Test-Automation.ps1 -DashboardSmoke -CaptureVisual -Language pl -Theme Light -WindowWidth 1280 -WindowHeight 720
.\scripts\Test-Automation.ps1 -DashboardSmoke -CaptureVisual -Language pl -AppTextScale 150
.\scripts\Test-Automation.ps1 -DashboardSmoke -CaptureVisual -Language en -AppTextScale 200
.\scripts\Test-Automation.ps1 -ClassicUiRecovery -CaptureVisual
```

`-DashboardFixture` seeds 100 explicitly synthetic QA entries in the isolated test
profile; it is not production sample data or evidence of a real source download.
The script captures its owned HWND and semantic trees for all eight routes, checks
runtime selection and persistence, and uses the real native app with fake media
tools. `-DashboardSwitchStress` performs twenty Classic → Modern cycles. Recovery
checks the saved Modern preference survives `--classic-ui`. Native physical DPI
and real banner/cold activation still require separate evidence.

`DashboardShellTests` covers v1 migration, schema rejection, selection/reset/restart,
constructor fallback, common notification/external routes, chrome disposal, singleton
runtime preservation, 100-item paging/reveal, logical size/text-scale layouts, action
contrast and bounded/sanitized telemetry. These tests do not replace visual QA.
