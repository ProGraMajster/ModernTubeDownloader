# Changelog

## 1.0.0 — 2026-10-06

### Added

- Source-neutral yt-dlp frontend with metadata and Details for Overview, Source, Metadata, Formats, Subtitles and Chapters.
- Runtime Supported services catalog from the active executable, version/path/hash cache, grouped search, technical mode and metadata-only Check URL.
- Paged playlist preview, per-entry selection, shared quality/options, duplicate skipping and ordered queue insertion.
- Independent LIVE page/store/scheduler and concurrency: FromNow, Stop and save, scheduled monitoring, reconnect, verified MKV Parts, Partial and restart recovery.
- Persistent VOD queue with bounded 1–3 concurrency, pause/cancel/retry/reorder, optional position numbers and auto-scroll.
- Matching-format VOD workspaces and manual restart resume; bounded transient retries with fresh metadata and optional pacing.
- Auto/MP4/MKV/WebM containers and FFmpeg stream-copy merge/remux without implicit media transcoding.
- Custom ranges; manual/automatic subtitle sidecars and compatible embedding; opt-in user-selected Netscape cookies.txt.
- Chrome/Edge YouTube extension source, modal quality choice, queue/open-in-app actions and /live/ routing.
- Opt-in authenticated Web Remote, numeric mobile range editor, queue controls and separate LIVE status/actions.
- Automatic yt-dlp, GPL FFmpeg/ffprobe and Deno provisioning, validation, atomic activation, rollback and update checks.
- History, optional full JSON sidecars and publication-date CreationTime; PL/EN, Light/Dark/System and Windows branding.
- MIT project license, self-contained Windows x64 packaging, separate symbols/checksum, CI and guarded tag-release automation.

### Changed

- Development, CI and release pin ModernFormsNext master `f521f9dfcfe601bf9b6199b88132cccb2380d1bf`, fetched 2026-10-06.
- LIVE ownership is independent of the ordinary queue; VOD pause and limits do not consume or stop LIVE work.
- Extractor availability is distinguished from per-source/per-feature verification.
- SponsorBlock Mark requires explicit MKV. Remove stays Experimental/default OFF with a visible known-defect warning.

### Fixed

- Incomplete metadata and repeated Details opening no longer depend on WinForms-style construction lifecycle assumptions.
- Shared navigation/card styles preserve theme/interactive text contrast; compact actions use localized short labels.
- Mobile range input no longer needs a colon on a numeric keyboard; Full mode does not submit stale hidden bounds.
- Safe opaque Auto/MKV selection requires actual input/post-remux A/V/duration validation; invalid remux output is rejected rather than marked Completed.
- External /live/ routing, SPA button lifetime and remembered modal quality use the existing validated transport.
- Child-process ownership and durable shutdown retain resumable VOD workspaces and verified LIVE recovery parts.

### Known limitations

- SponsorBlock Remove has confirmed edit-point playback defects and no stable-quality guarantee. Prefer Mark/MKV or leave it off.
- LIVE FromStart is Experimental/source-dependent; upcoming monitoring requires the running app.
- Stream-copy ranges may align to keyframes; ranges cannot combine with subtitles/SponsorBlock and Remove cannot combine with subtitles.
- Extractor presence does not guarantee every site/account/URL works. Cookies.txt is optional, user-supplied and not authenticated-media-qualified.
- Unknown terminated VOD tools require explicit Retry; general automatic process-crash retry is post-1.0 hardening.
- Exhaustive device/browser/DPI/source matrices and uninterrupted multi-hour soak remain post-1.0 QA.

Earlier implementation notes and dependency SHAs are preserved in [pre-release history](docs/development/pre-release-history.md). Scoped evidence remains in STABILIZATION_REPORT.md and VALIDATION_REPORT.md.
