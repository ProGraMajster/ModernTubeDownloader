# UI automation testing

ModernTubeDownloader uses the existing ModernFormsNext `AccessibleObject` tree through `ModernFormsNext.Automation` and its Windows named-pipe bridge. It does not maintain a second UI tree. The bridge is **off by default** and is compiled only into Debug builds. A normal `ModernTubeDownloader.exe` launch has no Automation endpoint; a Release build does not contain the Automation project references and rejects `--automation`.

## Start and discover

Build the Debug application and the CLI from the same pinned ModernFormsNext source checkout (`f521f9dfcfe601bf9b6199b88132cccb2380d1bf`, origin/master fetched 2026-10-06):

```powershell
dotnet build .\ModernTubeDownloader\ModernTubeDownloader.csproj -c Debug -m:1 /p:UseSharedCompilation=false
dotnet build .\.mfn-master-worktree\ModernFormsNext.Automation.Cli\ModernFormsNext.Automation.Cli.csproj -c Debug -m:1 /p:UseSharedCompilation=false
.\ModernTubeDownloader\bin\Debug\net10.0-windows\ModernTubeDownloader.exe --automation
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
| Main window and navigation | `MainWindow`, `NavDownloads`, `NavLive`, `NavHistory`, `NavSettings` |
| LIVE | `LiveUrlInput`, `LiveAnalyzeButton`, `LiveStartButton`, `LiveQualitySelector`, `LiveContainerSelector`, `LiveFromNowOption`, `LiveFromStartOption`, `LiveActiveList`, `LiveScheduledList`, `LivePartialList`, `LiveStatistics`, per-session `LiveStopSaveButton-<guid>`, `LiveCancelButton-<guid>`, `LiveResumeButton-<guid>` |
| Analysis | `DownloadUrlInput`, `PasteButton`, `AnalyzeButton`, `PreviewCard`, `QualitySelector`, `VideoRangeMode`, `VideoRangeStart`, `VideoRangeEnd`, `AddToQueueButton`, `DetailsButton` |
| Playlist preview | `PlaylistPreview`, `PlaylistTitle`, `PlaylistEntriesList`, `PlaylistEntryToggle<N>`, `PlaylistSelectedCount`, `PlaylistQualitySelector`, `PlaylistRangeMode`, `PlaylistRangeStart`, `PlaylistRangeEnd`, `PlaylistSelectAllButton`, `PlaylistClearSelectionButton`, `PlaylistPreviousPageButton`, `PlaylistNextPageButton`, `PlaylistPageCount`, `PlaylistAddSelectedButton`, `PlaylistAddResult` |
| Queue | `QueueList`, `QueuePauseButton`, `QueueMenuButton`, `QueueItem-<guid>`, `QueuePosition-<guid>` |
| Settings | `SettingsScroll`, `SettingsSaveButton`, `LanguageSelector`, `ThemeSelector`, `ConcurrencySelector`, `ShowQueueNumbersToggle`, `WebRemoteEnable`, `WebRemotePort`, `WebRemoteAddress`, `WebRemoteToken`, `WebRemoteCopyToken`, `WebRemoteRegenerateToken`, `WebRemoteShowToken`, `WebRemoteOpen`, `WebRemoteQr` |
| History | `HistorySearch`, `HistoryList` |
| Details | `DetailsWindow`, `DetailsTabs`, `DetailsOverviewTab`, `DetailsMetadataTab`, `DetailsFormatsTab`, `DetailsSubtitlesTab`, `DetailsChaptersTab` |
| Supported sources | `SupportedSourcesWindow`, `SupportedSourcesVersion`, `SupportedSourcesSearch`, `SupportedSourcesTechnicalToggle`, `SupportedSourcesList`, `SupportedSourcesPrevious`, `SupportedSourcesNext`, `SupportedSourcesUrlInput`, `SupportedSourcesCheckButton`, `SupportedSourcesResult`, `SupportedSourcesOpenButton`, `SupportedSourcesCloseButton` |

The Details `*Tab` IDs belong to ModernFormsNext's logical `TabItem` peers. Selecting one exposes the corresponding `Details*Content` page. IDs are independent of PL/EN text and screen geometry. Hidden views and inactive tab pages are not necessarily exposed until selected.

## Smoke and headless tests

Quality-dropdown diagnostics now write `quality-dropdown-native-trace.jsonl` in the isolated profile: iteration/timestamp, fresh canonical handle/states/actions, action result, foreground/active/focus/mouse-capture HWND and visible owned popup HWND/dimensions. Assertions are unchanged and the capture no longer inserts an arbitrary delay. Accepted Expand is an action result, not a promise that real outside clicks/window deactivation cannot close the popup before a later request. Preserve such failures and native evidence; do not retry Expand, reset focus, weaken assertions or mislabel correct Collapsed state as a stale peer. Native ComboBox is windowless; its popup/Form, not the control, owns HWNDs. The current minimal native 100-cycle variants and environmental-interruption trace are documented in STABILIZATION_REPORT.md.

Run `scripts\Test-Automation.ps1` after building the Debug app, the FakeTool test binary, and the CLI. The script creates an isolated temporary data root and synthetic tool executables. It verifies a normal launch without a bridge, then performs `find → inspect → action → wait → inspect` through the CLI for analysis, quality, queue, Settings and all Details tabs. It closes the dialog and app and verifies that discovery is removed. No real media is downloaded and no screenshot or coordinate click is used. The script reports its isolated profile path so logs can be inspected after a failure.

Optional `-CaptureVisual` inspects owned HWND captures without desktop-coordinate input. `-QualityDropdownSmoke -QualityDropdownPasses 20` checks fresh Expanded state and a supported Collapse action before each collapse, then verifies Collapsed. With capture enabled, the image comes from the same-process popup HWND (not the main window). Unsupported actions fail the test; they are not ignored or converted into success. Earlier PL/Light 1280×850 and EN/Dark 1000×720 runs passed 20 cycles on `bcf2bcb...`; the older pass-15 PL/Light failure lacked native trace and remains historically uncertain. The current minimal/production native 100-cycle variants pass with native/peer agreement. Three exact-CLI interruptions and the final two-cycle PL/Light FAIL are retained: real outside input/deactivation closes the popup before inspect, and the peer correctly reports Collapsed. A controlled owned-window witness confirms this mechanism. No confirmed framework/app bug or peer-accuracy limitation was found; no workaround or weakened assertion was added. Separate ordinary final EN/Dark smoke passes and does not erase the popup diagnostic failures. Detailed counts/profiles are in STABILIZATION_REPORT.md; full focus/DPI qualification is not claimed.

For playlists, run `scripts\Test-Automation.ps1 -PlaylistSmoke`. It analyzes a deterministic 12-entry fixture, excludes one unavailable item and one unchecked item, selects a quality, adds 10 queue jobs, checks duplicate skipping, playlist order, and semantic queue-number badges. `-PlaylistSmoke -PreviewOnly -PlaylistCount 220` checks native pagination while rendering at most 40 rows per page. `-PlaylistSmoke -PlaylistCount 220` exercises bulk insertion and persistence of 218 ordered queue items; for large queues the smoke queries specific automation IDs instead of requesting the CLI's bounded entire-tree response.

The `AutomationTestHostTests` use `ModernFormsNext.Testing` to create Details with full/missing metadata and a QueueItemCard without a native window. Six additional PL/EN × Light/Dark/System cases construct DownloadsView against isolated FakeTool AppServices and verify compact action labels, shared bindings and normal/hover/focus/pressed text contrast. They do not test the whole Downloads flow headlessly. SettingsView/HistoryView and broader service-backed behavior still use the isolated native CLI smoke. TestHost must be disposed synchronously on its creating thread before awaiting service shutdown. Native window startup, named-pipe security/lifetime, operating-system focus and actual rendering remain native smoke responsibilities. Screenshots are useful for visual layout, theme, DPI and clipping; they are not required for the basic functional path.

`scripts\Test-Automation.ps1 -LiveSmoke -SampleUrl https://example.test/live/active` invokes NavLive, analyzes a synthetic active stream, starts it explicitly, inspects its own statistics and Details, and invokes Stop and save. Use `-SampleUrl https://example.test/live/upcoming` for the scheduled list and separate persistence. `-Language pl|en`, `-Theme Light|Dark` and window size parameters exercise alternate presentation. Run native app smokes serially: the real single-instance transport intentionally forwards secondary launches.

For bounded real qualification, `-RealLiveSmoke` uses the Debug semantic UI. `scripts\Test-ReleaseLive.ps1` starts the self-contained published app with an isolated profile, observes the separate `/api/live` API, and verifies Stop and save, History, ffprobe A/V and a short decode. `-ParallelDownloads` adds three short public Blender VOD ranges through the production remote allowlist and asserts three ordinary active jobs while LIVE continues. `-RestartDuringCapture` checks manual resume; add `-AutoResumeAfterRestart` for the opt-in policy. `-InterruptDownloader` kills only the isolated yt-dlp capture child and checks retry into Part 2; add `-NoRetryAfterInterruption` for terminal Partial. Explicit validated local tool paths are required. These are not human A/V playback, browser-extension or physical LAN tests. Current results are in [VALIDATION_REPORT.md](VALIDATION_REPORT.md).
