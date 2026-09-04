# ModernTubeDownloader

<img src="ModernTubeDownloader/Assets/AppIcon.png" alt="ModernTubeDownloader icon" width="96" height="96">

ModernTubeDownloader is a Windows desktop application for analyzing and downloading media supported by [yt-dlp](https://github.com/yt-dlp/yt-dlp). The native interface is built with [ModernFormsNext](https://github.com/ProGraMajster/ModernFormsNext).

The application follows the complete single-item flow:

`URL → analysis → metadata → quality selection → queue → download → FFmpeg merge/remux → final file → history`

## Features

- URL analysis through structured yt-dlp metadata.
- Compact video summary and detailed Overview, Metadata, Formats, Subtitles, and Chapters views.
- Available-quality selection with compatible video/audio format matching.
- Persistent download queue with progress, pause, cancel, retry, ordering, removal, and automatic scrolling to a newly added item.
- Lossless FFmpeg stream-copy merge/remux where separate streams are required.
- Automatic first-run installation and later update checks for yt-dlp, FFmpeg, and ffprobe.
- Persistent download history with file and folder actions.
- Polish and English interface.
- System, Light, and Dark appearance modes.
- Optional full raw metadata JSON sidecar next to the final media file; enabled by default.
- Optional filesystem creation date derived from the media publication date; enabled by default.

## Requirements

The primary distribution is self-contained and requires:

- Windows 10 or Windows 11, x64.
- Network access on first start so the managed tools can be downloaded.

The release ZIP includes the .NET 10 runtime. Users do not need to install .NET, yt-dlp, FFmpeg, or ffprobe manually. ModernTubeDownloader does not bundle the external media tools in its ZIP; the application downloads and validates them under the current user's application-data directory.

Current yt-dlp guidance recommends an external JavaScript runtime plus `yt-dlp-ejs` for full YouTube support. ModernTubeDownloader 1.0.0 does not manage those optional components yet; extractor behavior can therefore depend on the current yt-dlp release and target site.

## Installation

1. Download `ModernTubeDownloader-1.0.0-win-x64.zip` from the release assets.
2. Extract the ZIP to a writable directory.
3. Run `ModernTubeDownloader.exe`.

Do not run the application directly from inside the ZIP.

## First start

The first start prepares the download engine automatically:

1. The application queries the official yt-dlp and yt-dlp FFmpeg-Builds release sources.
2. It downloads `yt-dlp.exe` and the selected GPL-configured FFmpeg/ffprobe archive into a private staging directory.
3. Published SHA-256 information is checked when available.
4. The package is extracted with traversal and expanded-size guards.
5. The executables are validated by running their version commands.
6. Only a verified version is activated.

If preparation fails, the application keeps any previously working version and presents a retry action. Tools are not downloaded again on every start; update checks are rate-limited to once per 24 hours unless the user explicitly requests one.

## Using the application

Open **Downloads**, paste a supported HTTP(S) URL, choose **Analyze**, select an available quality, and add the item to the queue. Completed files are written to the configured output directory. Separate streams are merged or remuxed by FFmpeg without re-encoding.

The **Settings** view controls directories, file conflicts, filename templates, sidecar metadata, publication-date timestamps, thumbnails, language, theme, and advanced custom-tool overrides. Custom executable paths are validated but are never replaced or updated automatically.

## Application data

Runtime state is stored under `%LOCALAPPDATA%\ModernTubeDownloader`:

- `settings.json` — user preferences and paths.
- `queue.json` — persisted queue state.
- `history.json` — completed-download history.
- `metadata\` — internal raw metadata cache.
- `thumbnails\` — thumbnail cache.
- `logs\ModernTubeDownloader-YYYYMMDD.log` — diagnostic logs.
- `Tools\state.json` — active managed-tool versions.
- `Tools\yt-dlp\versions\...` — downloaded yt-dlp versions.
- `Tools\ffmpeg\versions\...` — downloaded FFmpeg/ffprobe versions.
- `Tools\.staging\` — temporary tool downloads and extraction.

Media download temporary files and final output use the directories selected in Settings. The `MODERNTUBEDOWNLOADER_DATA_ROOT` environment variable is an integration-test seam and can redirect the application-data root for an isolated run.

## Privacy and network activity

ModernTubeDownloader does not require an account and contains no application-owned telemetry or analytics. It sends network requests only as required for:

- GitHub release metadata and managed-tool downloads for yt-dlp and FFmpeg-Builds;
- media analysis and downloads performed by yt-dlp;
- thumbnail retrieval from URLs returned in media metadata.

The contacted media services, CDNs, GitHub, and downloaded tools operate under their own terms and privacy policies. Diagnostic logs are local. Process logging redacts HTTP(S) URLs and common credential/cookie arguments, but users should still review logs before sharing them publicly.

## Build from source

Building requires the .NET 10 SDK and a checkout of ModernFormsNext at commit `1dce91b9740b3eba8d5f2f529017bebeffb88438`. By default the project expects that checkout in `.mfn-master-worktree`. A different location can be supplied through the `ModernFormsNextRoot` MSBuild property:

```powershell
dotnet restore .\ModernTubeDownloader.slnx -p:ModernFormsNextRoot=C:\src\ModernFormsNext
dotnet build .\ModernTubeDownloader.slnx -c Release -m:1 /p:UseSharedCompilation=false -p:ModernFormsNextRoot=C:\src\ModernFormsNext
dotnet test .\ModernTubeDownloader.slnx -c Release --no-build -m:1 /p:UseSharedCompilation=false -p:ModernFormsNextRoot=C:\src\ModernFormsNext
```

ModernFormsNext 1.10.0 on NuGet predates fixes required by this application. The source reference remains intentional until a compatible framework release is published. End users of the self-contained ZIP do not need the ModernFormsNext repository.

## Create the Windows release candidate

```powershell
.\scripts\Build-Release.ps1
```

The script restores, builds Debug and Release, runs the complete test suite, publishes self-contained `win-x64`, removes PDB files from the user package, creates a separate symbols archive, checks that managed tools and the test fake are absent, and produces the release ZIP plus SHA-256 file under `artifacts\release`.

The first release deliberately uses a stable multi-file, untrimmed, non-ReadyToRun publish. Single-file, trimming, NativeAOT, and ReadyToRun are disabled until they have dedicated compatibility validation with ModernFormsNext and its native dependencies.

## Legal

Use ModernTubeDownloader only for media you are allowed to download and in accordance with applicable law and service terms. The application does not implement DRM circumvention.

The ModernTubeDownloader project license has not yet been selected. Until a `LICENSE` file is committed, the generated ZIP is a local release candidate and must not be presented as a final public release. Third-party components retain their own licenses; see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Additional project information is available in [ARCHITECTURE.md](ARCHITECTURE.md), [CURRENT_STATUS.md](CURRENT_STATUS.md), [CHANGELOG.md](CHANGELOG.md), and [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
