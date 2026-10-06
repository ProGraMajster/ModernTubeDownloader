# Architecture

## Boundaries

The solution contains three projects:

- `ModernTubeDownloader` — the production .NET 10 Windows application and ModernFormsNext UI.
- `ModernTubeDownloader.Tests` — xUnit unit/integration tests.
- `ModernTubeDownloader.FakeTool` — a deterministic process-level test double built only for tests.

Production code is divided by responsibility:

- `Models` — yt-dlp metadata/formats, quality and format selection, queue state, history, and tool status.
- `Infrastructure` — application paths, atomic JSON, logging, and the shell-free asynchronous process runner. On Windows, each external tool is assigned to a kill-on-close Job Object so a spawned FFmpeg cannot retain redirected pipes after an unexpected yt-dlp exit.
- `Services` — settings, managed-tool provisioning, yt-dlp analysis/download, FFmpeg remux, queue/persistence/processing, history, and thumbnail cache.
- `Localization` and `Resources/Languages` — runtime JSON localization, culture selection, and English fallback.
- `Settings` — persisted configuration contracts.
- `Theming` — semantic color/brush tokens, registered Light/Dark themes, System-mode resolution, control recipes, and short transitions/effects.
- `Utilities` — guarded custom-argument tokenizer, file conflict handling, and explicit file/folder opening.
- `Views` — ModernFormsNext forms and user controls.
- `WebRemote` — optional in-process Kestrel adapter, authenticated bounded API, and embedded mobile HTML/CSS/JS; no second queue or download backend.

`MainForm` composes services and navigation. It does not contain download or process logic.

The opt-in `WebRemoteHost` is owned by `AppServices`. Settings changes reconcile a single Kestrel listener bound to one explicitly selected loopback/private IPv4 address; shutdown stops it before the queue worker and tool manager. The host calls the existing queue, processor, metadata, format selector, and thumbnail-cache services. Its endpoints return narrow presentation DTOs, never file paths or raw metadata. A per-process 256-bit token in an Authorization header, same-origin/Host checks, CSP, body/rate limits, and a YouTube-only HTTPS add allowlist define the MVP security boundary. There is no cookie authentication, UPnP, wildcard binding, WAN relay, or automatic firewall change. See [WEB_REMOTE.md](WEB_REMOTE.md) for operational details and HTTP caveats.

## UI composition

`MainForm` is a persistent shell with a sidebar, header/status area, content host, and queue-statistics footer. Navigation swaps only the content view and applies a short opacity transition. The shell keeps download/process ownership in services and receives state through existing events.

The views are code-first ModernFormsNext controls:

- `DownloadsView` owns the URL, preview-state presentation, queue toolbar, and responsive card host.
- `LiveView` observes `LiveSessionService` for analysis, active recordings, scheduled sessions and partial/recoverable sessions. It never owns a child process or scheduler.
- `QueueItemCard` maps queue state to one primary and one secondary action; less-frequent operations live in a context menu.
- `HistoryView` filters persisted records without changing history ownership.
- `SettingsView` edits the existing settings contract and delegates immediate language/theme application to services.
- `VideoDetailsForm` is rebuilt from the current localization whenever the runtime language changes.

`AppUi` centralizes card, button, input, label, and semantic-resource bindings. `AppAppearanceService` registers application themes with ModernFormsNext `ThemeManager`, uses parallel color and brush resources, and resolves System mode through the platform theme service. Resource transitions are deliberately short (220 ms); interaction feedback uses ripple and press-scale effects. No custom paint loop or second UI framework is introduced.

`LocalizationService` loads embedded flat JSON dictionaries for `pl` and `en`, verifies keys at test time, selects the persisted language, exposes a matching `CultureInfo`, and falls back to English for a missing translation. Views subscribe to one `LanguageChanged` event and refresh user-facing labels without restarting the application.

Responsive layout is controlled at the view/card boundary. The supported minimum form size is 1000×700; narrow or short layouts reduce nonessential vertical spacing, reflow queue-card actions, and retain scrolling for lists and settings.

## Managed tools

`ToolManager` is the single source of truth for yt-dlp, FFmpeg/ffprobe, and Deno availability. Its startup path is:

1. Load `Tools/state.json` and validate the active version or an explicit Advanced custom override.
2. If a managed tool is missing, query the official release source and stream its asset to `Tools/.staging`.
3. Verify content length and SHA-256 when the release publishes a digest/checksum.
4. Extract FFmpeg and Deno ZIPs with traversal and expanded-size guards. Validate yt-dlp, FFmpeg/ffprobe, and Deno 2.3.0+ as child processes.
5. Move the prepared directory into a versioned `Tools/<tool>/versions/<release>` location and atomically update the state manifest.

Release identity includes the asset digest, which is important for the rolling FFmpeg `latest` tag. The old version directory is retained. Therefore download, checksum, extraction, and process-validation failures cannot overwrite the active version.

`YtDlpProcessRunner` acquires leases for yt-dlp and Deno, then passes the verified Deno path explicitly through `--js-runtimes`; `FfmpegService` acquires FFmpeg only when a merge/remux is required. A verified update prepared during use becomes `UpdatePending`; the last released lease activates it. Custom overrides use the same validation boundary but skip all release and update operations.

One shared `HttpClient` is used for release metadata and streaming downloads. A successful lookup is recorded and automatic checks are limited to one per 24 hours. Analysis becomes available when yt-dlp and Deno are valid. A missing FFmpeg installation produces a degraded state: combined-stream downloads remain usable, while merge jobs fail at the FFmpeg capability boundary with a visible diagnostic.

## Data flow

### Runtime source information (2026-10-03)

`SupportedSourcesService → ToolManager yt-dlp lease → --list-extractors / --extractor-descriptions` is a read-only catalog path, separate from both schedulers. A lease carries an atomic version/managed/path identity captured under ToolManager's lock. The executable SHA-256 completes the memory-cache key. Relevant ToolManager changes invalidate the cache and refresh an open dialog; an update activated on lease release causes a new read, never a stale-version result. A failed new executable cannot fall back to an old list labelled current.

`SourceCheckService → existing YtDlpMetadataService → YtDlpProcessRunner` uses structured metadata only, a 50-entry flat playlist bound and cancellation. It has no queue, LIVE service or scheduler dependency. `MainForm` routes an explicit Open action to the existing Downloads/LIVE view; those views retain confirmation and existing ownership. An authoritatively detected playlist may enter the full playlist-analysis path even when its URL is not covered by a simple path heuristic.

`ExtractorIdentityService` maps friendly identities and actual metadata facts centrally. `VerifiedSourcesRegistry` is a small per-extractor/per-feature evidence registry, not a supported-site table. Family grouping is lexical presentation, not inferred VOD/LIVE/subtitle capability. `SupportedSourcesForm` filters data before creating at most 12 rows; expanded groups use a scrollable technical text area. The Source tab and compact card reuse `SourcePresentation`; original URLs omit credentials, fragments and query values. Extension transport and Web Remote allowlists are unchanged. See `SUPPORTED_SOURCES.md` for qualification boundaries.

1. `YtDlpMetadataService` validates absolute HTTP(S) URLs. Individual videos (including `watch?v=...&list=...`) use `--dump-single-json --skip-download --no-playlist`; explicit playlist URLs use `--yes-playlist --flat-playlist --skip-download --dump-single-json --ignore-errors` and a bounded cancellation timeout.
2. `VideoMetadata` maps useful single-video fields and format details. `JsonExtensionData` plus `RawJson` retain unmodeled metadata. `SubtitleTrackReader` projects available manual/automatic track codes and formats from the existing subtitle dictionaries without duplicating raw metadata. `PlaylistMetadata` projects only lightweight entry fields from a flat playlist response; the preview renders at most 40 checkbox rows per page.
3. `MediaAvailabilityPolicy` classifies yt-dlp `availability` and explicit `live_status` before selection. `was_live` is a downloadable replay if ordinary formats exist; `is_live`, `is_upcoming`, and `post_live` are not treated as VOD. `FormatSelector` then chooses the best video under the requested height. If that stream lacks audio, it chooses a high-quality audio-only format with container/codec compatibility preference. Playlist entries enter the ordinary queue without formats; each queue worker analyzes its individual URL and invokes this same selector before download.
   Verified streams are preferred over opaque candidates. For non-LIVE Auto/MKV only, a direct HTTP(S) format with positive dimensions, a known media extension and both codec names absent can be selected provisionally. `FormatSelection.RequiresStreamProbe` forces an actual local audio/video/positive-duration/format-name check before remux/final move and after any remux. Failure retains the recovery workspace and creates no final/history success. Explicit `none`, DRM markers, unknown protocols/extensions, active LIVE and explicit MP4/WebM are not eligible; no metadata codec facts or transcode are invented.
4. `DownloadQueueService` owns ordering and state independent of the UI. Playlist selection adds in `playlist_index` order with one batch notification, skipping nonterminal duplicates by video ID and quality. Queue position and playlist index remain separate. `QueueProcessor` admits up to the live 1–3 job setting, owns cancellation per item, and treats pause as “do not start another job.” Lowering the limit never terminates running work. Optional pacing schedules a new admission after an independently sampled inclusive min/max delay; the first item starts immediately, and active jobs are not paused.
5. `DownloadFailureClassifier` maps process exit/stderr, HTTP status, exception type, and stage to a category, retryability, localized message key, and sanitized technical summary. The processor retries only transient categories, with independent short backoff and a fixed attempt cap. `DownloadJobExecutor` reanalyzes metadata and formats after a failed attempt. Matching VOD formats reuse a stable job/format workspace so yt-dlp can continue compatible `.part` data; a changed format or disabled resume gets a fresh workspace. yt-dlp owns filename-template evaluation and Windows filename sanitization.
   A `MediaTimeRange` value travels with each queue item into persistence/retry/history. For a custom range, the executor validates known duration and active-live state and passes one `--download-sections` expression to each selected yt-dlp format invocation. `YtDlpProcessRunner` leases the managed FFmpeg path explicitly for those invocations. Separate cut streams then pass through the existing FFmpeg stream-copy merge; there is no implicit full-download/trim fallback or re-encode. After finalization, a bounded best-effort ffprobe call logs the actual container duration without failing an otherwise completed download. A shared playlist range is rejected before batch enqueue if a known selected entry is too short; unknown durations are checked when the worker analyzes that entry.

   `SubtitleOptions` and `SponsorBlockOptions` are copied into each queue item, persisted, and retained on retry. A subtitle-only yt-dlp request selects manual/automatic tracks and language codes after media preparation; matching files become sidecars and optionally pass through an FFmpeg subtitle-embedding step with video/audio stream copy. Missing subtitle languages and subtitle post-processing errors complete media with a warning. SponsorBlock Mark/Remove uses one combined yt-dlp format invocation so yt-dlp can modify both A/V streams together; the app does not request keyframe re-encoding. `DownloadOptionCompatibilityValidator` is shared by desktop, queue admission, worker, and Web Remote: custom range cannot combine with subtitles/SponsorBlock, Remove cannot combine with subtitles, and Mark requires explicit MKV. WebM and Auto Mark remain unsupported until real verification; Remove emits an experimental warning because direct yt-dlp output showed an edit-point timestamp defect. Playlist previews apply one policy to all selected jobs, then each worker resolves its own tracks.

   Opt-in desktop `UseCookieFile`/`CookieFilePath` stores only the location of a user-selected Netscape file. `CookieFileService` performs lightweight bounded header/size/readability validation at Settings save and before each operation, then adds `--cookies <path>` to single-video and playlist analysis, media, and subtitle yt-dlp requests; retry reanalysis uses the current desktop setting. The app does not extract browser databases or copy cookie files to app data. Process logging and crash sanitization redact the registered path, and Web Remote/browser-extension payloads and queue projections do not contain it.
6. A combined format moves directly to finalization. Separate video/audio files pass through `FfmpegService`, which maps one video and one audio stream and uses `-c copy` for a lossless remux.
7. Only a non-empty verified output is moved into the final directory. Conflict policy is applied at that final move.
8. Raw metadata and a history entry are persisted after success.

## Process contract

`AsyncProcessRunner` is the only low-level child-process owner:

- `UseShellExecute = false`
- arguments are added individually through `ProcessStartInfo.ArgumentList`
- stdout and stderr are drained concurrently
- cancellation kills the complete process tree and then awaits its actual exit
- process objects and registrations are disposed deterministically
- logged HTTP(S) URLs and common credential/cookie values are redacted

Normal yt-dlp console text is not parsed as progress. The application supplies a versioned prefix (`MTD_PROGRESS|`) through `--progress-template` and reads numeric fields. The downloaded path comes from `--print after_move:...` with a directory enumeration fallback.

FFmpeg writes machine-readable `-progress pipe:1` output. The final mux uses a temporary filename and source streams are never deleted on merge failure.

## Queue lifecycle

Persisted states are:

`Queued → Waiting → DownloadingVideo → DownloadingAudio → Merging → Finalizing → Completed`

Not every VOD job visits every state. Terminal alternatives are `Failed`, `Cancelled`, `Partial`, and `Interrupted`. Active jobs are restored as `Interrupted` on restart by default, preserving stable workspaces; optional VOD auto-resume changes them to `Queued`. LIVE is not part of this lifecycle. Old LIVE numeric enum values and the optional queue `LiveSession` field remain solely to read and migrate older installations; queue admission rejects new LIVE records.

Automatic retry uses `RetryFailedDownloads` (default on) and `AdditionalRetryAttempts` (default 2, clamped to 0–5). `2` means at most three total attempts. It does not retry permanent private/unavailable/unsupported/format/disk failures or cancellation. Each attempt records its number, failure category, and a short sanitized reason. Waiting for retry is cancellable and remains owned by the same queue item; it does not pass through the new-item admission gate. Manual retry clears failure state and reanalyzes formats. VOD jobs use `JobSessionId` and a format-keyed workspace so yt-dlp can reuse matching `.part` files; a new format is isolated. LIVE jobs use `LiveSession.SessionId`, refresh metadata before each capture, and keep independently probed/remuxed MKV parts. A new FFmpeg LIVE capture cannot safely append to an old muxed output, so reconnect uses Part 2 rather than pretending byte-perfect resume. Unplayable raw data stays in its workspace for recovery; only verified media is exposed as a final file/history entry.

Inter-item pacing uses `EnableInterDownloadDelay` (default off), `MinimumInterDownloadDelaySeconds` (default 3), and `MaximumInterDownloadDelaySeconds` (default 20), normalized to 0–300 seconds with max ≥ min. `IDownloadTiming` isolates clock, random selection, and retry waiting for deterministic tests. The delay is between starts of different items even with concurrency 2–3; it never occurs between video/audio streams, merge, or finalization of one item. Pause blocks admission, resume resamples the gate, and shutdown interrupts it.

Queue notifications distinguish structural/order changes from high-frequency item progress. Newly available work, completions, pause changes, and concurrency-setting changes wake one admission loop; progress lines do not create worker loops or a wake-up backlog.

## Independent LIVE ownership

Before this refactor, `DownloadQueueService` owned LIVE queue records, `QueueProcessor` polled upcoming sessions and performed capture retry/admission, and `DownloadJobExecutor` dispatched a LIVE special case. That consumed the ordinary concurrency budget.

After:

| Subsystem | State owner | Execution owner | Executor | Limit | Persistence |
| --- | --- | --- | --- | --- | --- |
| VOD | `DownloadQueueService` | `QueueProcessor` | `DownloadJobExecutor` | `MaxSimultaneousDownloads` | `queue.json` |
| LIVE | `LiveSessionService` | `LiveRecordingScheduler` | existing `LiveRecordingExecutor` | `MaxSimultaneousLiveRecordings` | `live-sessions.json` |

`LiveSessionService` owns the session list, Add, EditWaiting, StopAndSave, Cancel, Resume, state updates, atomic persistence and notifications. Its scheduler command boundary is internal; the UI and Web Remote call the central service. `LiveRecordingScheduler` owns each recording task, linked cancellation source, admission, reconnect/backoff and shutdown. It also owns one bounded short upcoming probe at a time through the existing `LiveAvailabilityProbe`, independently of recording slots. Neither scheduler references the other's queue/service, and there is no shared admission semaphore. Both may use the existing tools, process runner, cookie support, timing abstraction, failure classifier and History.

States: `WaitingForLive → Pending → Starting → Recording → Finalizing → Completed`, with `Reconnecting`, `Partial`, `Failed`, `Cancelled` and `Interrupted` alternatives. Waiting sessions retain quality/start intent and recheck immediately after load. Restart records become Interrupted unless the separate opt-in LIVE auto-resume setting permits admission. Lowering a limit waits for existing workers; raising it wakes only its own scheduler. A VOD pause never inhibits upcoming probes or recording.

The existing `LiveRecordingExecutor` keeps FromNow, eligible experimental FromStart, metadata refresh, Stop and save, verified MKV parts, partial recovery and Windows Job Object behavior. New stable workspaces are `Temp/LiveSessions/<id>/part-NNNN`; legacy `Temp/sessions/<id>` data remains recoverable. Failed capture finalizes verified Part 1 before retrying with fresh metadata into Part 2. It cannot safely append an FFmpeg capture to an old muxed file. Matching split A/V may be stream-copied only when ffprobe proves aligned starts and a common positive duration; `-t` caps the result to that interval. Unknown/mismatched streams are retained raw, not guessed or mixed across attempts. History contains only verified playable outputs with LIVE/Partial flags and part indices.

Migration writes the separate destination first, deduplicates by durable SessionId, then removes only LIVE source records from `queue.json`. Retrying migration is idempotent. Individual invalid session records are skipped/logged; a corrupt entire LIVE JSON gets a recovery copy. Neither raw metadata nor signed stream URLs are persisted with a LIVE session.

`DownloadsView`, `QueueItemCard` and the shell footer present only VOD. `LiveView` presents its own counters and session actions. `/api/queue` exposes only VOD; `/api/live` projects safe LIVE data and routes actions through `LiveSessionService` without workspace paths, cookies or raw metadata. Starting a long recording still requires desktop confirmation.

The architectural oracle enqueues five VOD jobs and three sessions at limits 3/2. It asserts exactly five independent active jobs, third LIVE waiting, independent slot release and limit changes. A process-level version additionally observes five distinct child PIDs. Tests also stop each scheduler independently and pause VOD while an upcoming session starts.

## Shutdown

The first close request is cancelled while shutdown runs asynchronously. VOD and LIVE schedulers cancel and await their own workers concurrently, including each worker's final durable save. Tool provisioning is stopped, both stores/settings/history are saved, and the form closes on the ModernFormsNext UI thread. Stopping one scheduler alone leaves the other operational.

## Storage

Settings, queue, history, and the active-tool manifest use small atomic JSON files (`.tmp` then replace). This keeps version 1 lightweight while leaving models suitable for migration to a database if library-scale search is later required. Thumbnail bytes are cached on disk by SHA-256 of the URL and decoded `SKBitmap` objects are reused in memory.

For isolated process/UI integration tests, `MODERNTUBEDOWNLOADER_DATA_ROOT` can redirect the application-data root. Fake-tool delay variables are test seams only and do not change production behavior when unset.

## Build and release packaging

Application and assembly versioning is centralized in `Directory.Build.props`. The application builds against a configurable `ModernFormsNextRoot`; local development defaults to the ignored `.mfn-master-worktree`, while CI checks out the exact `ModernFormsNextCommit` (`f521f9dfcfe601bf9b6199b88132cccb2380d1bf`, origin/master fetched 2026-10-06) into an ignored dependency directory. This source reference keeps development, CI, and release on the same framework revision; a NuGet migration is a separate decision.

The supported 1.0.0 distribution is self-contained `win-x64`. Publishing is deliberately multi-file, untrimmed, and without ReadyToRun or NativeAOT so ModernFormsNext reflection, resources, and native Skia dependencies retain their validated behavior. `scripts/Build-Release.ps1` creates a clean end-user directory, excludes PDB/test/fake-tool/managed-tool files, produces a separate symbols archive, and writes a SHA-256 checksum.

yt-dlp, FFmpeg/ffprobe, and Deno are never copied from a developer machine into the release. ToolManager provisions them after installation under the per-user application-data root.

The application is MIT-licensed, as explicitly selected by its owner. The tag workflow requires LICENSE, matching application/tag version, exact tag/commit identity and a public repository. The owner authorized the v1.0.0 publication on 2026-10-06; publication proceeds only after the final scoped regression, repository-history hygiene scan and master CI pass. Browser-store and NuGet publication remain out of scope.

## Extension points

- `VideoMetadata` is source-neutral; `SourceUrl` is not named `YouTubeUrl`.
- `FormatSelection` already records concrete IDs for a future advanced picker.
- The queue processor is independent of controls; its executor interface allows deterministic bounded-concurrency and cancellation tests without external downloads.
- Playlist entries use the existing per-item queue processor and history; automatic channel orchestration is intentionally not exposed.
