# ModernTubeDownloader

<img src="ModernTubeDownloader/Assets/AppIcon.png" alt="ModernTubeDownloader icon" width="96" height="96">

A Windows desktop application for analyzing, downloading and recording media through [yt-dlp](https://github.com/yt-dlp/yt-dlp), with a native [ModernFormsNext](https://github.com/ProGraMajster/ModernFormsNext) interface.

**[Download v1.0.0 for Windows x64](https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v1.0.0)** · [Changelog](CHANGELOG.md) · [Supported sources](SUPPORTED_SOURCES.md)

## Features

- **VOD:** URL analysis, metadata, available-quality selection, Auto/MP4/MKV/WebM containers and FFmpeg stream-copy merge/remux without hidden media transcoding.
- **Playlists:** paged preview, individual selection, shared quality/options, unavailable-entry filtering, duplicate skipping and ordered queue insertion. Each video resolves its actual formats when downloading.
- **Queue and recovery:** persistent jobs, 1–3 concurrent downloads, pause/cancel/retry/reorder, optional position numbers, auto-scroll, bounded transient retries and optional admission pacing. Matching-format workspaces retain resumable yt-dlp `.part` data; interrupted jobs default to manual resume.
- **Independent LIVE:** a separate page, scheduler, store and 1–3 recording limit. FromNow recording, Stop and save, scheduled-broadcast monitoring, fresh-metadata reconnect, verified MKV Parts and Partial recovery do not consume VOD slots. History is shared.
- **Supported services:** a runtime catalog from the active managed or custom yt-dlp, with search, grouped services, technical mode and metadata-only Check URL. Extractor availability does **not** mean every source, account, URL or feature has been verified by ModernTubeDownloader.
- **Details and history:** compact video/source summary, Overview/Source/Metadata/Formats/Subtitles/Chapters, completed-file history and file/folder actions.
- **Media options:** manual/automatic subtitle sidecars and compatible embedding; custom start/end ranges; optional full JSON sidecars and publication-date file CreationTime, both enabled by default.
- **Browser integration:** an unpacked Chrome/Edge extension for YouTube videos, Shorts, playlists and `/live/` links, with a quality-selection modal and explicit queue/open-in-app actions.
- **Web Remote:** opt-in authenticated, mobile-friendly LAN queue controls and LIVE status/actions. Numeric Hours/Minutes/Seconds range input avoids mobile colon-keyboard problems. Remote adds are YouTube-scoped.
- **Managed tools:** automatic installation, validation and update checks for yt-dlp, FFmpeg/ffprobe and Deno; advanced custom-executable overrides.
- **Appearance:** Polish/English and Light/Dark/System themes, shared application branding and responsive scrolling.

## Requirements and installation

Windows 10/11 **x64**, and internet access to install the tools on first start. The self-contained ZIP includes .NET 10: no separate .NET or manual yt-dlp/FFmpeg/Deno installation is required.

1. Download `ModernTubeDownloader-1.0.0-win-x64.zip` from [Releases](https://github.com/ProGraMajster/ModernTubeDownloader/releases).
2. Extract it to a writable folder. Do not run it from inside the ZIP.
3. Run `ModernTubeDownloader.exe`.

The ZIP contains the application and runtime, **not** downloaded media tools. First start downloads official yt-dlp, the GPL-configured yt-dlp FFmpeg-Builds archive and Deno into per-user AppData, checks available checksums, safely extracts them and validates executable versions. Failed updates retain the previous working version. Automatic checks are limited to once per 24 hours; installed tools are reused.

This first release is not code-signed. Windows may display an unknown-publisher/SmartScreen warning; verify the release source and published SHA-256 before deciding whether to run it.

## Quick start

In **Downloads**, paste a supported HTTP(S) URL → **Analyze** → choose an available quality/container → **Add to queue**. Quality presets mean “up to” the chosen resolution; source formats determine actual availability. Explicit MP4/WebM require stream-copy-compatible codecs; MKV supports a broader set. The default output is the current user's Videos/YouTube folder, configurable in Settings.

Use an explicit playlist URL to select multiple entries. A YouTube `watch?...&list=...` URL remains a single-video request. Channel expansion is not automatic.

**More options** configures per-job subtitles and SponsorBlock; Settings provides defaults. Custom ranges accept start, end or both, using section requests rather than a silent full-download fallback. Desktop bounds use `hh:mm:ss`; the remote editor uses separate numeric components. Stream-copy cuts can align to nearby keyframes rather than exact requested frames.

In **LIVE**, analyze a broadcast and explicitly confirm **Record from now** or **Waiting for LIVE**. The app must remain running to monitor a scheduled session; it normally polls every 30–60 seconds and rechecks after restart. **Stop and save** verifies a playable MKV part. Reconnect and restart recovery save subsequent parts separately, not unsafe append/concatenation. VOD pause does not pause LIVE. **FromStart is Experimental and source-dependent.**

## Optional integrations

### Chrome / Edge extension

Launch the desktop application once to register its per-user URI protocol. Get the extension source from this repository/source archive, then load `browser-extension/` as an unpacked extension. It is **not published to Chrome Web Store**. Clicking the YouTube page button opens a modal; only confirmation sends a request. `/live/` opens the app for explicit recording confirmation. The one-way URI transport does not expose real-time yt-dlp capabilities to the browser; browser quality choices are maximum-height presets. See [extension setup](browser-extension/README.md).

### Web Remote

Enable it in Settings and explicitly choose a private LAN IPv4 address for phone access. Scan the address-only QR and enter the session token separately. Authentication is on by default; the service is off by default. Use a **trusted LAN only**: HTTP is not encrypted. No automatic firewall rule, port forwarding, UPnP or WAN relay is created. Remote adds remain YouTube-restricted; new LIVE recordings require desktop confirmation. See [Web Remote](WEB_REMOTE.md).

### Cookies and SponsorBlock

Cookies are off by default. You may deliberately select your own Netscape-format `cookies.txt` in desktop Settings for authorized media. The app stores only its path, does not extract browser databases and does not copy cookie contents into AppData. Treat that file as an account credential; never share it. Authenticated-media behavior is source-dependent and has not been qualified with a user-supplied account file.

SponsorBlock is off by default. **Mark requires explicit MKV** and is the supported chapter-marking path. **Remove is Experimental, with confirmed edit-point playback defects including frozen video and malformed timing; there is no stable-quality guarantee.** It displays a warning and does not trigger a transcoding workaround. Prefer Mark/MKV or leave SponsorBlock off.

## Known limitations

- yt-dlp/site behavior can change; extractor availability is not an all-URL/all-feature guarantee. DRM circumvention is not implemented.
- FromStart depends on retained source fragments and is Experimental. Real upcoming/natural-end behavior is source-dependent; scheduled monitoring needs the app running.
- Resume depends on matching formats, source/protocol and yt-dlp support. An unexplained terminated yt-dlp process can become Failed/Unknown and require explicit Retry; retained partial data is preserved.
- Custom ranges cannot combine with subtitles or SponsorBlock; Remove cannot combine with subtitles. Active LIVE excludes these VOD post-processing options.
- Missing subtitle languages or embedding failures produce warnings without discarding completed media. No automatic media transcoding, part concatenation or channel expansion.
- Exhaustive DPI/device/browser/source matrices and uninterrupted multi-hour soak remain post-1.0 follow-up, not universal compatibility claims.

## Data and privacy

Settings, VOD queue, LIVE sessions, history, metadata/thumbnail caches, `logs/`, crash diagnostics and `Tools/` live under `%LOCALAPPDATA%\ModernTubeDownloader`. Temporary media defaults to the current user's Downloads/ModernTubeDownloader/Temp folder; final media uses Settings' output directory.

There is no application-owned telemetry or account requirement. Network activity includes tool-release metadata/downloads, yt-dlp's media services/CDNs and optional SponsorBlock requests, and metadata thumbnail retrieval. Web Remote listens only when explicitly enabled. Third-party services/tools have their own terms and privacy policies.

Logs are local and redact common credential/URL data. Review logs before sharing. Optional JSON sidecars intentionally contain **full original yt-dlp metadata**, which can include URLs or account-related details: review them before publishing or sharing.

## Build from source

Requires the .NET 10 SDK and Node.js for JavaScript tests. Development, CI and Release use ModernFormsNext `f521f9dfcfe601bf9b6199b88132cccb2380d1bf` (origin/master fetched 2026-10-06), not a private fork or copied framework source:

```powershell
git clone https://github.com/ProGraMajster/ModernTubeDownloader.git
cd ModernTubeDownloader
git clone --no-checkout https://github.com/ProGraMajster/ModernFormsNext.git .mfn-master-worktree
git -C .mfn-master-worktree checkout --detach f521f9dfcfe601bf9b6199b88132cccb2380d1bf
dotnet restore .\ModernTubeDownloader.slnx
dotnet build .\ModernTubeDownloader.slnx -c Debug -m:1 /p:UseSharedCompilation=false
```

Open `ModernTubeDownloader.slnx` in Visual Studio after preparing the dependency checkout. A direct project build can instead specify `-p:ModernFormsNextRoot=C:\src\ModernFormsNext`; the solution itself expects `.mfn-master-worktree`. End users of the ZIP need neither repository.

`scripts/Build-Release.ps1` validates and creates the self-contained Windows x64 ZIP, separate symbols ZIP and SHA-256 under ignored `artifacts/release`. Publishing is multi-file, untrimmed, without NativeAOT or ReadyToRun. Debug-only opt-in UI automation is documented in [AUTOMATION_TESTING.md](AUTOMATION_TESTING.md); it is absent from Release.

## License and responsible use

ModernTubeDownloader is [MIT licensed](LICENSE), Copyright © 2026 ProGraMajster. Third-party components and downloaded tools keep their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and the release's `licenses/` directory. The managed FFmpeg build is GPL-configured; the Windows yt-dlp executable has its own bundled-component notices.

Download or record only media you are permitted to access and use, in accordance with applicable law and service terms.

Developer information: [Architecture](ARCHITECTURE.md), [Current status](CURRENT_STATUS.md), [Release checklist](RELEASE_CHECKLIST.md), [Stabilization evidence](STABILIZATION_REPORT.md).
