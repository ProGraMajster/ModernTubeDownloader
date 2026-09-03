# Third-party dependencies and notices

This document records the third-party components used to build or operate ModernTubeDownloader 1.0.0. It is informational and is not legal advice. Each component remains subject to its own license terms.

ModernTubeDownloader's own project license has not yet been selected. That decision is separate from every license listed below and blocks a final public release.

## Runtime libraries shipped in the self-contained ZIP

### .NET 10 runtime

- Project: https://github.com/dotnet/runtime
- License: MIT, with additional third-party notices in the .NET distribution.
- License and notices: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT and https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

The self-contained `win-x64` artifact contains the Microsoft .NET runtime required to run the application without a separate .NET installation.

### ModernFormsNext

- Project: https://github.com/ProGraMajster/ModernFormsNext
- Source revision used for 1.0.0: `1dce91b9740b3eba8d5f2f529017bebeffb88438`
- License: MIT.

The application currently builds from a pinned source checkout because the required post-1.10.0 fixes are not yet available in a newer NuGet release. Published application output contains the resulting runtime assemblies, not the ModernFormsNext source tree.

### Markdig 1.3.2

- Project: https://github.com/xoofx/markdig
- NuGet license expression: BSD-2-Clause.

### Topten.RichTextKit 0.4.167

- Project: https://github.com/toptensoftware/RichTextKit
- NuGet license expression: Apache-2.0.

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

## Tools downloaded after installation

The application ZIP does not contain `yt-dlp.exe`, `ffmpeg.exe`, or `ffprobe.exe`. ToolManager downloads them into `%LOCALAPPDATA%\ModernTubeDownloader\Tools` for the current user.

### yt-dlp

- Project: https://github.com/yt-dlp/yt-dlp
- Managed release source: https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest
- Asset selected by the application: `yt-dlp.exe`.
- Core project license: The Unlicense.
- Core license: https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE
- Bundled executable notices: https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt

The official yt-dlp documentation states that PyInstaller-bundled executables contain GPLv3-or-later components and that the combined executable is distributed under GPLv3-or-later terms. The application's notices therefore do not describe the downloaded Windows executable as Unlicense-only.

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
