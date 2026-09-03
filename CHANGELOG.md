# Changelog

All notable changes to ModernTubeDownloader are documented in this file.

## 1.0.0 - Release candidate

### Application

- Added a ModernFormsNext-native Windows desktop interface with responsive Downloads, History, Settings, and Details views.
- Added runtime Polish/English localization and System/Light/Dark themes.
- Added URL validation and structured yt-dlp metadata analysis.
- Added a compact analyzed-video card, quality selection, and resilient Details sections for incomplete metadata.
- Added a persistent single-worker queue with progress, pause, cancellation, retry, ordering, removal, restart recovery, and automatic vertical scrolling to new items.
- Added separate-stream download plus FFmpeg stream-copy merge/remux.
- Added final-file conflict handling and configurable temporary-file cleanup.
- Added persistent history and file/folder actions.

### Managed tools

- Added automatic first-run provisioning and 24-hour update checks for yt-dlp, FFmpeg, and ffprobe.
- Added streamed downloads, available SHA-256 verification, guarded archive extraction, executable validation, atomic activation, rollback preservation, and update deferral while tools are busy.
- Added advanced custom executable overrides without automatic replacement of user-provided tools.

### Metadata and files

- Added optional full raw metadata JSON sidecars next to final media files, enabled by default.
- Added optional final-file creation timestamps derived from yt-dlp publication metadata, enabled by default.
- Added atomic UTF-8 metadata writes and non-destructive warnings when post-processing fails.

### Release engineering

- Added centralized `1.0.0` assembly and file versioning.
- Added a deterministic self-contained Windows x64 publish profile without trimming, ReadyToRun, NativeAOT, or single-file bundling.
- Added reproducible packaging, separate symbols, CI, and guarded tag-release workflows.
- Added dependency, privacy, legal, release-status, and validation documentation.
