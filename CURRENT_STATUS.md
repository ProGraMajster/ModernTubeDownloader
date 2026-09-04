# Current status

Status date: 2026-09-04.

## Production-ready scope

- Native ModernFormsNext Windows desktop UI on .NET 10.
- Official multi-resolution Windows application icon shared by the executable and application windows.
- URL analysis, metadata normalization, quality selection, single-item queueing, stream downloads, FFmpeg merge/remux, finalization, and persistent history.
- Automatic yt-dlp and GPL-configured FFmpeg/ffprobe provisioning with validation, atomic activation, rollback preservation, and update checks.
- Persistent queue with pause, cancellation, retry, ordering, removal, automatic vertical scrolling, and safe restart recovery.
- Compact analyzed-video presentation and Details sections for overview, metadata, formats, subtitles, and chapters.
- Polish/English localization and System/Light/Dark themes.
- Full raw metadata sidecars and publication-date file timestamps, independently configurable and enabled by default.
- Local AppData logging and a fatal-error boundary that records full exceptions and displays a readable fallback dialog.
- Self-contained, multi-file, untrimmed Windows x64 publishing and deterministic ZIP packaging.

## Tested

- Debug and Release solution builds with serialized MSBuild and shared compilation disabled.
- Complete automated unit/integration suite, including tool installation/update failure paths, queue persistence, details null safety, localization parity, timestamp/sidecar post-processing, and scroll planning.
- Deterministic process-level workflow: analysis → separate streams → FFmpeg service merge → final file → metadata sidecar → timestamp → history.
- Native-window visual checks at 1440×900 and 1280×720 in Polish/English and Light/Dark, including a long Unicode title.
- Repeated Details opening with full and incomplete synthetic metadata, plus a live analyzed item; every tab, vertical scrolling, resize, close, and reopen were observed.
- Release output launch against isolated application-data roots, including successful clean-profile provisioning, no redundant second-start download, installed-tools offline startup, and readable first-start offline recovery UI.
- Live Big Buck Bunny download at 360p: separate video/audio acquisition, FFmpeg merge/remux, valid full JSON sidecar, publication-date CreationTime, history entry, and restart persistence.

See [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) for the exact current validation state. Automated and observed manual evidence are intentionally reported separately.

## Known limitations

- The public project license is not selected; the generated package is a local release candidate, not an approved public release.
- The source repository is private at `ProGraMajster/ModernTubeDownloader`; no tag or GitHub Release has been published.
- The application intentionally uses a pinned ModernFormsNext source checkout. NuGet 1.10.0 predates required scroll and windowing fixes.
- ToolManager does not yet manage `yt-dlp-ejs` or a JavaScript runtime recommended by current yt-dlp guidance for full YouTube support.
- Queue execution is intentionally single-worker. `MaxSimultaneousDownloads` is persisted but values above one do not yet create parallel workers.
- Interrupted jobs restart from the beginning; byte-range resume is not implemented.
- Playlists, channels, subtitle downloading, cookie/login UI, audio extraction, media conversion, embedding, cloud sync, and a library view are outside 1.0.0.
- Dedicated 125% and 150% DPI observation remains pending.

## Planned after 1.0.0 approval

- Select and commit the application license.
- Move to a compatible ModernFormsNext NuGet release when the required master changes are published.
- Complete the remaining premiere/live-date, DPI, 20+ item queue, and history shell-action manual matrix before tagging.
- Only after approval: create tag `v1.0.0` and allow the guarded release workflow to publish the GitHub Release.
