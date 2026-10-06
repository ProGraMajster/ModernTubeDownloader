# Historical pre-release development notes

This is the preserved pre-publication changelog snapshot. It documents earlier implementation rounds and their then-current dependency SHAs, not current release gates or support guarantees. The public 1.0.0 changelog is [CHANGELOG.md](../../CHANGELOG.md).

# Changelog

All notable changes to ModernTubeDownloader are documented in this file.

## Unreleased — stabilization (2026-10-04, local working tree)

- Updated the audited ModernFormsNext development/CI/release pin to `bcf2bcb1726cbb697d2e56600188c4ef2c91ebe1`; no framework patch.
- Added conservative Auto/MKV direct-media fallback when both codec names are missing, requiring real ffprobe A/V/duration validation before finalization and after remux. Explicit MP4/WebM no longer accept contradictory audio codec facts based on extension alone. No hidden transcode or LIVE fallback.
- Replaced phone-hostile colon-delimited Web Remote range inputs with separate numeric Hours/Minutes/Seconds, localized preview/errors and compatible API serialization. Full media no longer sends stale hidden bounds. Added 25 client regressions, HTTP asset assertions and CI execution.
- Added strict native quality-popup repetition and popup-HWND visual capture; 20 cycles pass on the current framework. Current stabilization evidence, failed/partial checks and release blockers are recorded separately from historical qualification.
- Restored existing shared styling on video-card actions and the compact localized More options label. Corrected on-accent text contrast in Dark and secondary pressed text in Light; six PL/EN × Light/Dark/System style/contrast regressions cover the card.

## Unreleased — runtime supported sources (2026-10-03, local working tree)

- Added a dynamic catalog from the active managed/custom yt-dlp, version/path/hash caching, automatic invalidation on tool updates, grouped search, technical mode, bounded pagination and exact upstream broken markers.
- Added Supported services entry points in Downloads and Settings, metadata-only Check URL, localized auth/private/unsupported results and explicit navigation without unattended queueing/recording.
- Added centralized source identity and metadata facts to the compact preview, LIVE analysis and Details / Source; retained source-neutral desktop behavior and YouTube-only extension/Web Remote security boundaries.
- Added conservative per-extractor/per-feature QA evidence for previously tested YouTube and Twitch functions; unknown/new sources remain yt-dlp Supported. Added the actual Vimeo logged-in diagnostic to authentication classification.
- Separated desktop LIVE replay navigation from the YouTube-only external-protocol validator. Source facts now explain missing codec identifiers on analyzed formats without altering FormatSelector or pretending arbitrary-source downloads are verified.
- Added parser/cache/update/custom/process/metadata/semantic UI regressions and real multi-platform metadata qualification. See `SUPPORTED_SOURCES.md`; no public release or source support guarantee is implied.

## Unreleased — independent LIVE subsystem refactor (2026-10-03, local working tree)

- Advanced the validated source pin from `4b0191d1c0de33e31e46c9f7a377dab833ae3d20` to `6d341963dfa0468c3636ea251e107ee34d0d1272` (+4 upstream commits: DPI events and Form.Activate); development, CI and release use the same SHA.
- Changed runtime ownership to `LiveSessionService → LiveRecordingScheduler → existing LiveRecordingExecutor`, separate from `DownloadQueueService → QueueProcessor → DownloadJobExecutor`. Added atomic `live-sessions.json` and idempotent legacy migration, independent 1–3 LIVE concurrency, cancellation, restart and upcoming monitoring.
- Added the LIVE page with active/scheduled/recoverable groups, explicit confirmation, duration/bytes/speed, independent statistics, details and central Stop/Cancel/Resume actions. Downloads cards/footer and `/api/queue` now present VOD only; `/api/live` has separate safe DTOs and actions.
- Preserved FromNow, eligible experimental FromStart, fresh-metadata reconnect, MKV Parts/Partial, cookies, Job Object and VOD workspaces. Added conservative matching split-stream common-duration recovery; no unsafe append, concatenation or transcode.
- Added mandatory 5 VOD + 3 LIVE ownership/limit tests, actual five-child-process evidence, independent pause/shutdown tests, migration, cancellation, shared History completion ordering and split A/V regressions. See `VALIDATION_REPORT.md` for final test and real-media results; historical entries below refer to earlier implementations.

## Unreleased — ModernFormsNext master and LIVE recording (local working tree)

- Updated the development/CI/release ModernFormsNext source pin to `4b0191d1c0de33e31e46c9f7a377dab833ae3d20` from the fetched `origin/master`.
- Added explicit active-LIVE FromNow and experimental FromStart queueing, persistent scheduled-LIVE waiting, and UI statuses/actions for recording, reconnecting, Stop and save, Partial and Interrupted.
- Added stable VOD session workspaces for matching-format `.part` continuation, fresh LIVE metadata on retry, verified stream-copy MKV part recovery, separate parts rather than unsafe capture concatenation, and per-part History records. Turning off partial-file preservation removes only the raw LIVE session workspace after final failure, not verified final MKV parts.
- Routed browser-extension `/live/` links to desktop analysis rather than quick queueing; Web Remote now presents LIVE states and offers stop/save on existing recordings while requiring desktop confirmation to start one.
- Added deterministic fake-tool and native semantic smoke coverage. Bounded real Twitch FromNow capture, Stop and save, self-contained Release restart (manual and automatic resume), controlled yt-dlp-process interruption/reconnect, and exhausted-retry Partial each produced verified MKV parts. A Windows Job Object prevents an orphan FFmpeg from holding tool output pipes after yt-dlp exits unexpectedly. Human playback, real FromStart, scheduled auto-start, real VOD restart and full visual/DPI review remain open. No public release was made.

## Unreleased — subtitles, browser-profile authentication, SponsorBlock (local working tree)

- Added per-job and shared playlist subtitle options for manual/automatic tracks, language codes, sidecars, safe subtitle-only conversion, and optional MP4/MKV/WebM embedding without media transcoding. Missing tracks or subtitle post-processing failures warn without discarding downloaded media.
- Added desktop-only opt-in `--cookies-from-browser` configuration for supported local browsers. Analysis, playlist expansion, download, retry, and subtitle requests share the setting; cookie values are neither persisted nor exposed to the extension or Web Remote.
- Added default-off SponsorBlock chapter marking and explicit segment removal through yt-dlp. Time ranges cannot be combined with subtitles/SponsorBlock, and removal cannot be combined with subtitles until timeline synchronization is verified.
- Added a compact advanced-options dialog, Settings sections, default-only Web Remote switches, fake-tool workflow tests, and native automation coverage. No release artifact has been rebuilt or published for this update.

## Unreleased — LAN remote and media ranges (local working tree)

- Added an off-by-default in-process LAN web remote with explicit private IPv4 binding, per-process token authentication, address-only QR, responsive PL/EN queue/add UI, polling, basic queue actions, bounded requests, and integration tests. Its HTTP transport remains appropriate only for trusted LANs.
- Added full/custom start/end media ranges to analyzed video and playlist previews, queue persistence/retry/history, queue cards, and the web form. Custom jobs use yt-dlp `--download-sections` with a leased FFmpeg binary and the existing stream-copy merge/remux pipeline; no hidden full-download fallback or automatic transcoding was added.
- Added deterministic range and HTTP regression tests. Real-site section accuracy/transfer, mobile LAN, firewall, and DPI observations remain release gates.

## Unreleased — live routes and download resilience (local working tree)

- Accepted YouTube `/live/<id>` links in the extension and desktop protocol. Completed replays with downloadable formats follow the ordinary queue path; active, upcoming, and still-processing broadcasts receive distinct localized messages rather than implied download support.
- Added centralized download-failure classification and sanitized user-facing reasons/technical summaries. Structured queue logs include attempt, stage, format, quality, container, duration, HTTP status, and outcome while redacting URLs and common secrets.
- Added bounded automatic retries for transient failures (default: two extra attempts), fresh metadata/format analysis and per-attempt temporary directories; private, unavailable, unsupported, format, disk, and cancelled jobs are not retried automatically.
- Added optional randomized inter-item admission pacing (off by default; inclusive 3–20 second defaults), independent of retry backoff and compatible with concurrency 1–3.
- Added deterministic fake-tool replay/403 recovery, scheduler, classifier, settings, and extension tests. Real completed-live and private-playlist browser observations remain pending.

## Unreleased — local browser integration

- Updated the unpacked Chrome/Edge extension with remembered “up to” quality, dark/light native-select styling, separate quick-queue and open-in-app actions, and explicit playlist auto-queue.
- Extended the validated browser request with action and container fields. Open-in-app analyzes the URL and exposes the application’s exact yt-dlp-derived quality/container options; the one-way custom URI deliberately does not claim dynamic browser-side capabilities.
- Added persisted default and per-job `Auto`, MP4, MKV, and WebM output-container selection. MP4/WebM filter incompatible streams, MKV remains permissive, and FFmpeg uses stream copy only; unsupported combinations fail without hidden transcoding.
- Added automatic per-user protocol registration/repair with a Settings disable/repair control and immediate queueing through the existing format selector and processor. The bounded same-user pipe remains separate from Debug automation.
- Documented the independently reproduced ModernFormsNext hide/show scrolling defect in framework issue #130; no app-side scroll workaround was added.

## Unreleased — local playlist work

- Added explicit playlist URL analysis through lightweight yt-dlp flat metadata, without changing single-video `watch?v=...&list=...` behavior.
- Added paged playlist preview with availability-aware selection, one quality preset, localized count/summary, and semantic automation IDs.
- Added ordered batch queue insertion with duplicate skipping, per-entry format analysis in the existing workers, and playlist context in queue/history persistence.
- Added deterministic playlist parser, queue, concurrency, TestHost, and Automation CLI coverage; no tag or release has been created for this work.

## 1.0.0 - Release candidate

### Application

- Added official multi-resolution Windows application branding for the executable, taskbar, title bar, Alt+Tab, and Details window.
- Added a ModernFormsNext-native Windows desktop interface with responsive Downloads, History, Settings, and Details views.
- Added runtime Polish/English localization and System/Light/Dark themes.
- Added URL validation and structured yt-dlp metadata analysis.
- Added a compact analyzed-video card, quality selection, and resilient Details sections for incomplete metadata.
- Added a persistent bounded-concurrency queue (1–3 simultaneous jobs) with independent progress, pause, per-item cancellation, live limit changes, retry, ordering, removal, restart recovery, and automatic vertical scrolling to new items.
- Added separate-stream download plus FFmpeg stream-copy merge/remux.
- Added final-file conflict handling and configurable temporary-file cleanup.
- Added persistent history and file/folder actions.

### Managed tools

- Added automatic first-run provisioning and 24-hour update checks for yt-dlp, FFmpeg/ffprobe, and Deno 2.3.0+.
- Added explicit Deno routing for yt-dlp's bundled EJS challenge solver without relying on global PATH.
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
