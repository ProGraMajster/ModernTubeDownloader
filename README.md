# ModernTubeDownloader

<img src="ModernTubeDownloader/Assets/AppIcon.png" alt="ModernTubeDownloader icon" width="96" height="96">

A Windows desktop application for analyzing, downloading and recording media through [yt-dlp](https://github.com/yt-dlp/yt-dlp), with a native [ModernFormsNext](https://github.com/ProGraMajster/ModernFormsNext) interface.

**[Download v1.2.1 for Windows x64](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.2.1)** · [Changelog](CHANGELOG.md) · [Supported sources](SUPPORTED_SOURCES.md)

Stable release target: **1.2.1**. Published assets and workflow results are authoritative on
the release page. Historical releases remain unchanged. See [cookie synchronization](COOKIE_SYNC.md),
[release checklist](RELEASE_CHECKLIST.md) and [yt-dlp feature audit/roadmap](YTDLP_FEATURE_GAP_AUDIT.md).

## New in 1.2.1

This stability/performance patch adds storage-safe queue pause/recovery, bounded notification
fallback/lifetime and friendly streaming Batch JSON envelopes. Large Batch handoffs run off
the UI context; queue rendering uses 50-card pages, filtered actions, cancellation and
advisory progress/size/transfer estimates. See [Batch import](BATCH_IMPORT.md) and the
[historical hotfix qualification](HOTFIX_1_2_1_CANDIDATE_1_REPORT.md).
Source builds pin the committed MFN notification hotfix in
[PR #271](https://github.com/ProGraMajster/ModernFormsNext/pull/271), based on current master.
The exact committed pin is retained for this patch; no framework merge/publication is implied.

### Retained from 1.2.0

Adds [tray/autostart](TRAY_AND_STARTUP.md), [SHA-verified application updates](UPDATES.md),
Other/[Batch import](BATCH_IMPORT.md), [versioned appearance profiles and text scaling](PERSONALIZATION.md),
and eleven categorized Settings pages. End users of the self-contained ZIP need no
framework checkout. Text scaling is separate from Windows DPI; two framework composite
controls have the [known typography limits](#known-limitations) described below.

- Tray supports restore, queue pause/resume, LIVE navigation and work-aware exit confirmation.
  Windows autostart/hidden start is opt-in and registration repair is explicit.
- Application updates use stable GitHub Releases, SHA-256 verification and a separate
  wait-for-exit installer with backup/rollback; media-tool updates remain independent.
- Other opens Batch import: TXT/CSV/JSON preparation, bounded analysis, paging/filtering,
  saved sessions and explicit handoff to the existing queue.
- Personalization adds live 80–200% text scaling, presets, semantic colors, per-page
  presentation options and validated JSON profile import/export/reset.

## Features

- **VOD:** URL analysis, metadata, available-quality selection, Auto/MP4/MKV/WebM containers and FFmpeg stream-copy merge/remux without hidden media transcoding.
- **Playlists:** paged preview, individual selection, shared quality/options, unavailable-entry filtering, duplicate skipping and ordered queue insertion. Each video resolves its actual formats when downloading.
- **Queue and recovery:** persistent jobs, 1–3 concurrent downloads, pause/cancel/retry/reorder, optional position numbers, auto-scroll, bounded transient retries and optional admission pacing. Matching-format workspaces retain resumable yt-dlp `.part` data; interrupted jobs default to manual resume.
- **Independent LIVE:** a separate page, scheduler, store and 1–3 recording limit. FromNow recording, Stop and save, scheduled-broadcast monitoring, fresh-metadata reconnect, verified MKV Parts and Partial recovery do not consume VOD slots. History is shared.
- **Supported services:** a runtime catalog from the active managed or custom yt-dlp, with search, grouped services, technical mode and metadata-only Check URL. Extractor availability does **not** mean every source, account, URL or feature has been verified by ModernTubeDownloader.
- **Details and history:** compact video/source summary, Overview/Source/Metadata/Formats/Subtitles/Chapters, completed-file history and file/folder actions.
- **Media options:** manual/automatic subtitle sidecars and compatible embedding; custom start/end ranges; optional full JSON sidecars and publication-date file CreationTime, both enabled by default.
- **Browser integration:** PL/EN unpacked Chrome/Edge extension with universal HTTP/HTTPS toolbar and page/link context-menu handoff. YouTube video/Short/playlist/LIVE buttons retain their centered quality modal. Desktop yt-dlp checks support; new LIVE recordings and generic playlists require desktop confirmation/selection.
- **Web Remote:** PL/EN/Auto mobile-friendly LAN queue controls and LIVE status/actions, one-time short pairing codes, HttpOnly sessions and optional explicit HTTPS. Numeric Hours/Minutes/Seconds range input avoids mobile colon-keyboard problems. Remote adds remain YouTube-scoped.
- **Managed tools:** automatic installation, validation and update checks for yt-dlp, FFmpeg/ffprobe and Deno; advanced custom-executable overrides.
- **Appearance:** Polish/English and Light/Dark/System themes, shared application branding and responsive scrolling.
- **Optional system notifications:** independently configurable Downloads/LIVE events, bounded progress updates, privacy mode, cached thumbnails, safe actions and automatic Windows backend selection. OS policy can prevent display even when submission succeeds.
- **Opt-in browser cookies:** user-selected HTTPS domains or metadata-only Custom selection, standalone Native Messaging, DPAPI storage, exclusive None/Manual/Synced sources and attempt-owned Netscape jars. With JIT consent, an authentication failure requests a fresh accepted snapshot and allows exactly one automatic retry. Account/session validity remains source-dependent.

## Installation

Windows 10/11 **x64**, and internet access to install the tools on first start. The self-contained ZIP includes .NET 10: no separate .NET or manual yt-dlp/FFmpeg/Deno installation is required.

1. Download `ModernTubeDownloader-1.2.1-win-x64.zip` from [Releases](https://github.com/ProGraMajster/ModernTubeDownloader/releases).
2. Extract it to a writable folder. Do not run it from inside the ZIP.
3. Run `ModernTubeDownloader.exe`.

The ZIP contains the application and runtime, **not** downloaded media tools. First start downloads official yt-dlp, the GPL-configured yt-dlp FFmpeg-Builds archive and Deno into per-user AppData, checks available checksums, safely extracts them and validates executable versions. Failed updates retain the previous working version. Automatic checks are limited to once per 24 hours; installed tools are reused.

This release is not code-signed. Windows may display an unknown-publisher/SmartScreen warning; verify the release source and published SHA-256 before deciding whether to run it.

## Updates

Settings → Application updates checks stable releases. Choose Notify only,
Auto-download or Install on exit; installation waits for real shutdown and preserves
local data. Verify published checksums; they are not code signing. See [UPDATES.md](UPDATES.md).

## Tray & startup

Settings → Startup configures minimize/close behavior and optional start with Windows.
The tray restores the same window and controls the existing queue/LIVE. Active work
requires confirmation before exit. See [TRAY_AND_STARTUP.md](TRAY_AND_STARTUP.md).

## Other / Batch Import

Other → Batch Import prepares TXT/CSV/JSON URL lists with bounded analysis, statuses,
filters, pages and saved sessions. Review ready rows and explicitly add them to the
existing queue. See [BATCH_IMPORT.md](BATCH_IMPORT.md).

## Personalization

Settings → Personalization provides presets, semantic colors, density and per-page
presentation. Import/export/reset uses validated local JSON and never resets jobs
or authentication. See [PERSONALIZATION.md](PERSONALIZATION.md).

### Text size

Choose 80–200% live application text size or reset to 100%. This does not change
Windows DPI or stretch icons/images. See the composite-control limits below.

## Downloads

In **Downloads**, paste a supported HTTP(S) URL → **Analyze** → choose an available quality/container → **Add to queue**. Quality presets mean “up to” the chosen resolution; source formats determine actual availability. Explicit MP4/WebM require stream-copy-compatible codecs; MKV supports a broader set. The default output is the current user's Videos/YouTube folder, configurable in Settings.

## Playlists

Use an explicit playlist URL to select multiple entries. A YouTube `watch?...&list=...` URL remains a single-video request. Channel expansion is not automatic.

**More options** configures per-job subtitles and SponsorBlock; Settings provides defaults. Custom ranges accept start, end or both, using section requests rather than a silent full-download fallback. Desktop bounds use `hh:mm:ss`; the remote editor uses separate numeric components. Stream-copy cuts can align to nearby keyframes rather than exact requested frames.

## LIVE

In **LIVE**, analyze a broadcast and explicitly confirm **Record from now** or **Waiting for LIVE**. The app must remain running to monitor a scheduled session; it normally polls every 30–60 seconds and rechecks after restart. **Stop and save** verifies a playable MKV part. Reconnect and restart recovery save subsequent parts separately, not unsafe append/concatenation. VOD pause does not pause LIVE. **FromStart is Experimental and source-dependent.**

## Browser extension

Launch the desktop application once to register its per-user URI protocol. Get the extension source from this repository/source archive, then load `browser-extension/` as an unpacked extension. It is **not published to Chrome Web Store**. Clicking the YouTube page button opens a modal; only confirmation sends a request. `/live/` opens the app for explicit recording confirmation. The one-way URI transport does not expose real-time yt-dlp capabilities to the browser; browser quality choices are maximum-height presets. See [extension setup](browser-extension/README.md).

## Web Remote

Enable it in Settings and explicitly choose a private LAN IPv4 address for phone access. Scan the address-only QR and enter the short one-time pairing code separately (10-minute expiry). Authentication is on by default; the service is off by default. HTTP is default and **not encrypted**: use a trusted LAN only. Optional HTTPS requires explicit certificate generation and manual per-device trust/fingerprint verification; no automatic root trust or firewall rule, port forwarding, UPnP or WAN relay is created. Remote adds remain YouTube-restricted; new LIVE recordings require desktop confirmation. See [Web Remote](WEB_REMOTE.md).

## System notifications

Optional download/LIVE events, progress, cached thumbnails, privacy and trusted-state actions are configured in Settings. Windows policy/provider capabilities govern delivery; Accepted is not proof of a visible banner. See [notification deployment and limitations](SYSTEM_NOTIFICATIONS.md).

## Cookies/authentication

Cookies are off by default. Choose either your own Netscape `cookies.txt` (path only, user-owned) or explicitly consent to browser synchronization. The extension reads only granted selected domains; the separate NativeHost stores snapshots using Windows DPAPI CurrentUser under `Auth/`. MTD never reads browser databases or combines both sources. Values are credentials and are not shown in UI/logs or sent through the URL protocol/Web Remote. See [consent, registration, deletion and limitations](COOKIE_SYNC.md). Authenticated-media behavior is source-dependent and has not been qualified with an account session.

SponsorBlock is off by default. **Mark requires explicit MKV** and is the supported chapter-marking path. **Remove is Experimental, with confirmed edit-point playback defects including frozen video and malformed timing; there is no stable-quality guarantee.** It displays a warning and does not trigger a transcoding workaround. Prefer Mark/MKV or leave SponsorBlock off.

## Known limitations

- Text scaling: some tab labels and numeric editors do not fully follow global scaling
  due to ModernFormsNext [#204](https://github.com/ProGraMajster/ModernFormsNext/issues/204)
  and [#205](https://github.com/ProGraMajster/ModernFormsNext/issues/205). These are accepted
  post-1.2 follow-ups, not crashes; no framework workaround is included.
- Polski: niektóre etykiety kart i edytory liczb nie podążają w pełni za globalnym
  skalowaniem tekstu z powodu ograniczeń ModernFormsNext #204/#205. Poprawa jest planowana
  po osobnych naprawach frameworka; ten hotfix nie zmienia tych kontrolek.
- yt-dlp/site behavior can change; extractor availability is not an all-URL/all-feature guarantee. DRM circumvention is not implemented.
- FromStart depends on retained source fragments and is Experimental. Real upcoming/natural-end behavior is source-dependent; scheduled monitoring needs the app running.
- Resume depends on matching formats, source/protocol and yt-dlp support. An unexplained terminated yt-dlp process can become Failed/Unknown and require explicit Retry; retained partial data is preserved.
- Custom ranges cannot combine with subtitles or SponsorBlock; Remove cannot combine with subtitles. Active LIVE excludes these VOD post-processing options.
- Missing subtitle languages or embedding failures produce warnings without discarding completed media. No automatic media transcoding, part concatenation or channel expansion.
- Exhaustive DPI/scale/page/theme, login/tray soak, clean-PC update and multi-source Batch,
  physical-device and native picker matrices remain post-1.2 QA, not universal compatibility claims.

## Supported services

Use Supported services to search the runtime catalog from the selected yt-dlp executable and check a URL without downloading. Generic HTTP/HTTPS acceptance is not a certification of private/account/DRM behavior or every extractor. See [SUPPORTED_SOURCES.md](SUPPORTED_SOURCES.md).

## Security/privacy

Settings, VOD queue, LIVE sessions, history, metadata/thumbnail caches, `logs/`, crash diagnostics and `Tools/` live under `%LOCALAPPDATA%\ModernTubeDownloader`. Opt-in encrypted cookie profiles and public native manifests use `Auth/`; temporary user-only credential jars use `Auth/tmp/` and are removed at the operation boundary or safe abandoned-jar cleanup. Temporary media defaults to the current user's Downloads/ModernTubeDownloader/Temp folder; final media uses Settings' output directory.

There is no application-owned telemetry or account requirement. Network activity includes application/tool GitHub release metadata/downloads, yt-dlp's media services/CDNs and optional SponsorBlock requests, and metadata thumbnail retrieval. Application update checks can be disabled in Settings. Appearance, Updates and Batch data stay in the application's local data directory. Web Remote listens only when explicitly enabled. Third-party services/tools have their own terms and privacy policies.

Logs are local and redact common credential/URL data. Known auth values are scrubbed before process callbacks/errors and metadata output; header/cookie/password/authorization metadata fields are removed recursively. Optional JSON sidecars retain other original metadata and can still include source URLs/account-related details: review them and logs before sharing. This is not a guarantee against every unknown upstream secret or forensic recovery.

## Build from source

Requires the .NET 10 SDK and Node.js for JavaScript tests. The current ModernFormsNext pin is `8d8a7679779608b4edbee7dc687c13e3b1a611d9` ([notification hotfix PR #271](https://github.com/ProGraMajster/ModernFormsNext/pull/271)), based on master `18f47b085a59e65eb611e3fd1d38e8dffa3fb19c` fetched 2026-10-15. The pin includes the reviewed notification fix, so no uncommitted framework patch is needed. No framework source is copied into this repository:

```powershell
git clone https://github.com/ProGraMajster/ModernTubeDownloader.git
cd ModernTubeDownloader
git clone --no-checkout https://github.com/ProGraMajster/ModernFormsNext.git .mfn-master-worktree
git -C .mfn-master-worktree checkout --detach 8d8a7679779608b4edbee7dc687c13e3b1a611d9
dotnet restore .\ModernTubeDownloader.Tests\ModernTubeDownloader.Tests.csproj
dotnet build .\ModernTubeDownloader.Tests\ModernTubeDownloader.Tests.csproj -c Debug -m:1 /p:UseSharedCompilation=false
```

Open `ModernTubeDownloader.slnx` or the app project in Visual Studio after preparing the dependency checkout. A direct project build can instead specify `-p:ModernFormsNextRoot=C:\src\ModernFormsNext`; solution references expect `.mfn-master-worktree`. The solution includes the independent Updater. End users of the ZIP need neither repository.

`scripts/Build-Release.ps1` validates and creates the self-contained Windows x64 ZIP, separate symbols ZIP and SHA-256 under ignored `artifacts/release`. Publishing is multi-file, untrimmed, without NativeAOT or ReadyToRun. Debug-only opt-in UI automation is documented in [AUTOMATION_TESTING.md](AUTOMATION_TESTING.md); it is absent from Release.

The application uses `net10.0-windows10.0.26100.0` with explicit `WindowsSdkPackageVersion=10.0.28000.87` before the first restore. The ZIP contains .NET, not a bundled Windows App Runtime installation. Notifications use the official MFN Automatic provider: App SDK when available, Classic WinRT/Shell fallback or Unavailable otherwise. Missing optional notification runtime must not prevent application startup. See [deployment and limits](SYSTEM_NOTIFICATIONS.md#windows-deployment).

## License and responsible use

ModernTubeDownloader is [MIT licensed](LICENSE), Copyright © 2026 ProGraMajster. Third-party components and downloaded tools keep their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the release's `licenses/` directory. The managed FFmpeg build is GPL-configured; the Windows yt-dlp executable has its own bundled-component notices.

Download or record only media you are permitted to access and use, in accordance with applicable law and service terms.

Developer information: [Architecture](ARCHITECTURE.md), [Current status](CURRENT_STATUS.md), [Release checklist](RELEASE_CHECKLIST.md), [Stabilization evidence](STABILIZATION_REPORT.md).
