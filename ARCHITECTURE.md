# Architecture

## Boundaries

The solution contains three projects:

- `ModernTubeDownloader` — the production .NET 10 Windows application and ModernFormsNext UI.
- `ModernTubeDownloader.Tests` — xUnit unit/integration tests.
- `ModernTubeDownloader.FakeTool` — a deterministic process-level test double built only for tests.

Production code is divided by responsibility:

- `Models` — yt-dlp metadata/formats, quality and format selection, queue state, history, and tool status.
- `Infrastructure` — application paths, atomic JSON, logging, and the shell-free asynchronous process runner.
- `Services` — settings, managed-tool provisioning, yt-dlp analysis/download, FFmpeg remux, queue/persistence/processing, history, and thumbnail cache.
- `Localization` and `Resources/Languages` — runtime JSON localization, culture selection, and English fallback.
- `Settings` — persisted configuration contracts.
- `Theming` — semantic color/brush tokens, registered Light/Dark themes, System-mode resolution, control recipes, and short transitions/effects.
- `Utilities` — guarded custom-argument tokenizer, file conflict handling, and explicit file/folder opening.
- `Views` — ModernFormsNext forms and user controls.

`MainForm` composes services and navigation. It does not contain download or process logic.

## UI composition

`MainForm` is a persistent shell with a sidebar, header/status area, content host, and queue-statistics footer. Navigation swaps only the content view and applies a short opacity transition. The shell keeps download/process ownership in services and receives state through existing events.

The views are code-first ModernFormsNext controls:

- `DownloadsView` owns the URL, preview-state presentation, queue toolbar, and responsive card host.
- `QueueItemCard` maps queue state to one primary and one secondary action; less-frequent operations live in a context menu.
- `HistoryView` filters persisted records without changing history ownership.
- `SettingsView` edits the existing settings contract and delegates immediate language/theme application to services.
- `VideoDetailsForm` is rebuilt from the current localization whenever the runtime language changes.

`AppUi` centralizes card, button, input, label, and semantic-resource bindings. `AppAppearanceService` registers application themes with ModernFormsNext `ThemeManager`, uses parallel color and brush resources, and resolves System mode through the platform theme service. Resource transitions are deliberately short (220 ms); interaction feedback uses ripple and press-scale effects. No custom paint loop or second UI framework is introduced.

`LocalizationService` loads embedded flat JSON dictionaries for `pl` and `en`, verifies keys at test time, selects the persisted language, exposes a matching `CultureInfo`, and falls back to English for a missing translation. Views subscribe to one `LanguageChanged` event and refresh user-facing labels without restarting the application.

Responsive layout is controlled at the view/card boundary. The supported minimum form size is 1000×700; narrow or short layouts reduce nonessential vertical spacing, reflow queue-card actions, and retain scrolling for lists and settings.

## Managed tools

`ToolManager` is the single source of truth for yt-dlp, FFmpeg, and ffprobe availability. Its startup path is:

1. Load `Tools/state.json` and validate the active version or an explicit Advanced custom override.
2. If a managed tool is missing, query the official release source and stream its asset to `Tools/.staging`.
3. Verify content length and SHA-256 when the release publishes a digest/checksum.
4. For FFmpeg, extract the ZIP with traversal and expanded-size guards. Validate yt-dlp, FFmpeg, and ffprobe as child processes.
5. Move the prepared directory into a versioned `Tools/<tool>/versions/<release>` location and atomically update the state manifest.

Release identity includes the asset digest, which is important for the rolling FFmpeg `latest` tag. The old version directory is retained. Therefore download, checksum, extraction, and process-validation failures cannot overwrite the active version.

`YtDlpProcessRunner` and `FfmpegService` acquire a `ToolUsageLease` before launching a child process. A verified update prepared during use becomes `UpdatePending`; the last released lease activates it. Custom overrides use the same validation boundary but skip all release and update operations.

One shared `HttpClient` is used for release metadata and streaming downloads. A successful lookup is recorded and automatic checks are limited to one per 24 hours. Missing first-run tools are prepared before analysis/download is enabled; an already valid local engine becomes usable before the background due-check completes.

## Data flow

1. `YtDlpMetadataService` validates an absolute HTTP(S) URL and invokes yt-dlp with `--dump-single-json --skip-download --no-playlist`.
2. `VideoMetadata` maps useful fields and format details. `JsonExtensionData` plus `RawJson` retain unmodeled metadata.
3. `FormatSelector` chooses the best video under the requested height. If that stream lacks audio, it chooses a high-quality audio-only format with container/codec compatibility preference.
4. `DownloadQueueService` owns ordering and state independent of the UI. `QueueProcessor` runs one job at a time; pause means “do not start the next job.”
5. `YtDlpDownloadService` downloads each selected format into its own attempt subdirectory. yt-dlp owns filename-template evaluation and Windows filename sanitization.
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

Not every job visits every state. Terminal alternatives are `Failed` and `Cancelled`. A failed/cancelled item can be retried. On application restart, a state that was active is safely restored as `Queued`; no claim of byte-level resume is made.

Queue notifications distinguish structural/order changes from high-frequency item progress. Only a newly available queued item wakes the processor, avoiding a wake-up backlog for progress lines.

## Shutdown

The first close request is cancelled while shutdown runs asynchronously. The processor cancellation token and tool-provisioning lifetime token are triggered, active work is awaited, queue/settings/history are saved, and then the form closes on the ModernFormsNext UI thread.

## Storage

Settings, queue, history, and the active-tool manifest use small atomic JSON files (`.tmp` then replace). This keeps version 1 lightweight while leaving models suitable for migration to a database if library-scale search is later required. Thumbnail bytes are cached on disk by SHA-256 of the URL and decoded `SKBitmap` objects are reused in memory.

For isolated process/UI integration tests, `MODERNTUBEDOWNLOADER_DATA_ROOT` can redirect the application-data root. Fake-tool delay variables are test seams only and do not change production behavior when unset.

## Build and release packaging

Application and assembly versioning is centralized in `Directory.Build.props`. The application builds against a configurable `ModernFormsNextRoot`; local development defaults to the ignored `.mfn-master-worktree`, while CI checks out the exact `ModernFormsNextCommit` into an ignored dependency directory. This source reference remains necessary because the published ModernFormsNext 1.10.0 package predates framework fixes required by the application.

The supported 1.0.0 distribution is self-contained `win-x64`. Publishing is deliberately multi-file, untrimmed, and without ReadyToRun or NativeAOT so ModernFormsNext reflection, resources, and native Skia dependencies retain their validated behavior. `scripts/Build-Release.ps1` creates a clean end-user directory, excludes PDB/test/fake-tool/managed-tool files, produces a separate symbols archive, and writes a SHA-256 checksum.

yt-dlp and FFmpeg/ffprobe are never copied from a developer machine into the release. ToolManager provisions them after installation under the per-user application-data root.

The tag workflow has a hard `LICENSE` prerequisite. Until the application license and repository visibility are explicitly approved, the generated package is a local release candidate and no public-release automation should be triggered.

## Extension points

- `VideoMetadata` is source-neutral; `SourceUrl` is not named `YouTubeUrl`.
- `FormatSelection` already records concrete IDs for a future advanced picker.
- The queue processor is independent of controls; bounded multi-worker processing can be introduced later around this service.
- Playlist/channel orchestration can add jobs without changing process ownership or per-item execution.
