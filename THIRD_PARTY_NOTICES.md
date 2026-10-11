# Third-party dependencies and notices

This document records the third-party components used to build or operate ModernTubeDownloader 1.0.0. It is informational and is not legal advice. Each component remains subject to its own license terms.

ModernTubeDownloader is licensed under MIT (Copyright © 2026 ProGraMajster); see LICENSE. That license is separate from each third-party license below. The release includes exact redistribution license/notice texts under `licenses/`.

## Runtime libraries shipped in the self-contained ZIP

### .NET 10 runtime

- Project: https://github.com/dotnet/runtime
- License: MIT, with additional third-party notices in the .NET distribution.
- License and notices: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

The package also contains the ASP.NET Core shared runtime used by the local Web Remote host (MIT plus its own third-party notices). Exact runtime-pack licenses/notices are included alongside those for .NET Core under `licenses/`.

The self-contained `win-x64` artifact contains the Microsoft .NET runtime required to run the application without a separate .NET installation.

### ModernFormsNext

- Project: https://github.com/ProGraMajster/ModernFormsNext
- Source revision for 1.0.0: `f521f9dfcfe601bf9b6199b88132cccb2380d1bf` (origin/master fetched 2026-10-06).
- Release 1.1.0 revision: `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3` (origin/master fetched 2026-10-11). Framework license remains MIT; source code is not copied into this repository.
- License: MIT.

The application currently builds from a pinned source checkout to keep development, CI, and release on the same validated revision. Application output contains the resulting runtime assemblies, not the ModernFormsNext source tree.

### Markdig 1.3.2

- Project: https://github.com/xoofx/markdig
- NuGet license expression: BSD-2-Clause.
- Redistribution text: `licenses/Markdig-1.3.2.txt`, from the NuGet-recorded source revision `fc705234fa211d179ee1d5e7656b51ab99f70ca9`.

### Topten.RichTextKit 0.4.167

- Project: https://github.com/toptensoftware/RichTextKit
- NuGet license expression: Apache-2.0.
- Redistribution text: `licenses/Topten.RichTextKit-0.4.167.txt`, from the NuGet-recorded source revision `ae434c82d8a197e3d259761879fb3f862f12efd8`.
- Full Apache 2.0 terms: `licenses/Apache-2.0.txt` (standard license text from GitHub's license API); the preceding file preserves RichTextKit's own copyright notice.

### SkiaSharp and HarfBuzz packages

- SkiaSharp 3.119.2 and SkiaSharp.NativeAssets.Win32 3.119.2.
- SkiaSharp.HarfBuzz 2.88.7.
- HarfBuzzSharp and HarfBuzzSharp.NativeAssets.Win32 7.3.0.1.
- Project: https://github.com/mono/SkiaSharp
- SkiaSharp package license: MIT.

The packages include additional native third-party material. Consult the `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt` files supplied by the exact NuGet packages when redistributing the compiled application.

### Microsoft platform libraries

- System.Drawing.Common 10.0.10 — MIT.
- Microsoft.Win32.SystemEvents 10.0.10 — part of the .NET libraries; see the .NET runtime license and notices above.
- System.Security.Cryptography.ProtectedData 10.0.0 — MIT; candidate.5 Windows CurrentUser DPAPI. The exact package `.nuspec` preserves MIT attribution/copyright and repository commit `b0f34d51fccc69fd334253924abd8d6853fad7aa`; its `THIRD-PARTY-NOTICES.TXT` and the full required .NET runtime MIT license are copied into `licenses/`. This package does not contain a standalone LICENSE file.

Release 1.1.0 ships a separate self-contained .NET runtime with the standalone NativeHost under `native-host/`, intentionally duplicating runtime files. The same exact runtime license/notices cover redistribution; no user-specific manifests, registry data or cookie store is bundled.

### Windows notification dependencies (1.1.0)

- Official MFN AppSdk/WinRT runtime assemblies: MIT, as part of ModernFormsNext.
- Microsoft.WindowsAppSDK.Base 2.0.4, Foundation 2.3.12, InteractiveExperiences 2.1.9 and Runtime 2.5.1: **Microsoft Windows App SDK software license terms**, not the application's MIT license. Exact restored package `license.txt` and `NOTICE.txt` are copied into the ZIP's `licenses/`; no App Runtime installer is bundled or auto-installed.
- Microsoft.Windows.SDK.NET.Ref 10.0.28000.87: Windows SDK .NET projection/targeting pack. The exact `.nuspec` with Microsoft's [Windows SDK license link](https://aka.ms/WinSDKLicenseURL) is retained in `licenses/`. SDK tooling/build tools are not the end-user application.
- WinRT.Runtime (C#/WinRT support) uses the [CsWinRT MIT license](https://github.com/microsoft/CsWinRT/blob/master/LICENSE), included as `licenses/CsWinRT-MIT.txt`.

Windows native components/runtime installations have their own Microsoft terms. A self-contained .NET application is not a self-contained Windows App Runtime deployment. See [Windows deployment](SYSTEM_NOTIFICATIONS.md#windows-deployment).

### QRCoder 1.8.0

- Project: https://github.com/Shane32/QRCoder
- License: MIT (see the package license and project repository).

QRCoder creates an address-only QR code locally in Settings. No online QR generation service is contacted, and the authentication token is not encoded in the QR.

## Tools downloaded after installation

The application ZIP does not contain `yt-dlp.exe`, `ffmpeg.exe`, `ffprobe.exe`, or `deno.exe`. ToolManager downloads them into `%LOCALAPPDATA%\ModernTubeDownloader\Tools` for the current user.

### yt-dlp

- Project: https://github.com/yt-dlp/yt-dlp
- Managed release source: https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest
- Asset selected by the application: `yt-dlp.exe`.
- Core project license: The Unlicense.
- Core license: https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE
- Bundled executable notices: https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt

The official yt-dlp documentation states that PyInstaller-bundled executables contain GPLv3-or-later components and that the combined executable is distributed under GPLv3-or-later terms. The application's notices therefore do not describe the downloaded Windows executable as Unlicense-only.

The official PyInstaller-bundled `yt-dlp.exe` also includes the `yt-dlp-ejs` challenge-solver scripts; no separate EJS package is downloaded by the application.

### Deno

- Project: https://github.com/denoland/deno
- Managed release source: https://api.github.com/repos/denoland/deno/releases/latest
- Asset selected on Windows x64: `deno-x86_64-pc-windows-msvc.zip`.
- License: MIT.
- License: https://github.com/denoland/deno/blob/main/LICENSE.md

Deno is downloaded as the JavaScript runtime recommended by yt-dlp for its EJS challenge solver. ModernTubeDownloader validates that the runtime is at least version 2.3.0 and passes its explicit executable path to yt-dlp; it does not rely on a global PATH entry.

### FFmpeg and ffprobe

- Project: https://ffmpeg.org/
- Build source: https://github.com/yt-dlp/FFmpeg-Builds
- Managed release source: https://api.github.com/repos/yt-dlp/FFmpeg-Builds/releases/latest
- Asset pattern selected on Windows x64: `ffmpeg-master-latest-win64-gpl.zip`.
- FFmpeg legal information: https://ffmpeg.org/legal.html

The selected archive is explicitly the GPL-configured variant. FFmpeg is generally LGPL 2.1-or-later, but GPL components make the GPL apply to this build. The FFmpeg-Builds repository's scripts are MIT-licensed; that does not change the license of the resulting FFmpeg binaries.

Because the tools are downloaded separately at runtime rather than redistributed inside the ModernTubeDownloader ZIP, their exact version, configuration, embedded notices, and corresponding-source obligations must be evaluated from the actual release asset. A future decision to bundle or mirror either tool requires a separate licensing review.

## Test-only dependencies

The repository uses Microsoft.NET.Test.Sdk 17.14.1, xUnit 2.9.3, and xunit.runner.visualstudio 3.1.5 for development and CI. The `ModernTubeDownloader.FakeTool` executable is a deterministic test double. None of these test artifacts is shipped in the user release ZIP.
