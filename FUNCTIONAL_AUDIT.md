# ModernTubeDownloader — functional audit

Audit date: 2026-09-08\
Application branch: `master` at `f206f5c7f80a540e17126337f6e4be2e97a5a2ef`, with uncommitted user work preserved\
ModernFormsNext: clean detached checkout of current `origin/master` at `6e3a7cfd148915f51b305aa17296d41fbd9222c1`\
Application version: `1.0.0`

## 2026-09-27 subtitle/authentication/SponsorBlock addendum (local working tree)

The historical audit below predates these local changes. Subtitle download, sidecar/embedding controls, browser-profile cookie selection, and SponsorBlock Mark/Remove are now implemented through existing queue workers; desktop Settings and per-job advanced options expose the controls. The Web Remote can request only saved subtitle/SponsorBlock defaults and cannot inspect cookie settings; the extension continues to send URL/action only. Fake-tool and native automation coverage do not establish real-site subtitle, SponsorBlock cut, browser-profile, Chrome, or mobile-LAN behavior. Custom time range plus subtitles/SponsorBlock and SponsorBlock removal plus subtitles are deliberately disallowed pending timeline verification. No release artifact includes this addendum yet.

## 2026-09-20 playlist addendum (local working tree)

The 2026-09-08 audit below remains a historical snapshot. Playlist expansion, listed there as not exposed, is now implemented locally for explicit playlist URLs: flat metadata analysis, paged selection, ordered duplicate-aware batch queueing, per-entry format analysis in ordinary workers, and playlist context in history. YouTube `watch?v=...&list=...` continues as one video. Deterministic parser/integration/TestHost/Automation CLI validation is tracked separately from real-site verification; no public release has been made for this addition.

## 2026-09-26 resilience addendum (local working tree)

The historical 61-group audit and its 2026-09-08 counts below were not rerun as a fresh whole-project audit. New local behavior accepts YouTube `/live/<id>` through the extension and protocol, classifies yt-dlp active/upcoming/processing/replay states, and permits completed replay downloads when normal formats exist. The browser route rejection was confirmed; no real completed-live or private-playlist URL was available for site-level smoke. No stable browser-side privacy signal has been established, so yt-dlp remains the final authority for private-playlist errors.

Download failure handling now has one classifier, localized safe UI messages, sanitized technical details, structured diagnostic fields, bounded transient-only retry with fresh format analysis, and optional random admission pacing. New fake-tool and scheduler tests provide automated evidence; the manual Chrome, real-live, and full pacing UI matrix remain pending before release approval.

## Executive verdict

The application is not a shell made of placeholders. Its primary single-video workflow is connected from UI handlers through real services to process execution, file finalization, post-processing, persistence, and history. After the remediation pass, all 61 audited functional groups are fully implemented: 61 fully implemented, 0 partially implemented, 0 currently broken user-exposed groups, and 0 unresolved dead/obsolete/suspicious code groups.

The two P1 gaps identified by the initial audit are closed. `QueueProcessor` is now a bounded, event-driven scheduler whose live limit is the persisted 1/2/3 setting, with per-item ownership, cancellation, failure isolation, pause admission control, and deterministic shutdown. `ToolManager` now provisions or validates Deno 2.3.0+, and every yt-dlp invocation receives the verified runtime path through `--js-runtimes` without depending on global `PATH`.

## Evidence and limits

- Static trace: every view, dialog, visible control factory, event handler, setting, queue operation, tool manager path, process invocation, persistence service, post-processor, localization resource, and release pin was inspected.
- Automated current-state evidence: restore, Debug build, Release build, and 94 tests passed against ModernFormsNext `6e3a7cf...` with zero build warnings and zero errors.
- Process-level workflow evidence: current tests execute analysis, separate stream downloads, FFmpeg merge, finalization, JSON sidecar, timestamp, metadata cache, history, cancellation, tool install/update paths, and restart recovery using the repository's deterministic external-process fake.
- Observed UI evidence on current code: in addition to the earlier English/Light, Details, resize, localization, theme, and 22-item scrolling checks, the remediation smoke used PL/Dark at 1440×900, a paused three-item queue, Resume, three simultaneously active jobs with independent progress, different completion paths, Deno/custom-tool readiness in Settings, and one manually cancelled active job. The queue persisted three completed records plus the later completed and cancelled records with localized status keys. After `origin/master` advanced, the final binary was rebuilt against MFN `6e3a7cf...`; its 1440×900 PL/Dark startup, Ready state, persisted queue render, and clean close were observed again.
- A real network download was not repeated on 2026-09-08. Repository evidence from 2026-09-04 contains a live Big Buck Bunny analysis/download/merge/sidecar/timestamp/history run, but this report does not relabel that dated evidence as a current live test.
- At this audit date, playlist expansion, multiple-URL input, subtitle download, embedding metadata, transcoding, notifications, and clearing completed history were not exposed. Playlist expansion was subsequently added as described in the 2026-09-20 addendum; the other items remain outside scope.

## UI-to-backend feature map

| # | User-visible feature | UI → handler | Service/backend | Result |
|---:|---|---|---|---|
| 1 | Downloads navigation | `MainForm` navigation button → `ShowView` | Shows existing `DownloadsView` with animated transition | Fully implemented |
| 2 | History navigation | `MainForm` → `ShowView` | Refreshes persisted `HistoryService` snapshot on visibility | Fully implemented |
| 3 | Settings navigation | `MainForm` → `ShowView` | Binds live `SettingsService`, `ToolManager`, appearance, and localization state | Fully implemented |
| 4 | First-run preparation and Retry | `MainForm.RetryPreparation` | `ToolManager.RetryAsync` → release source → installer → binary validator | Fully implemented |
| 5 | Engine/queue footer status | queue/tool events → `MainForm.UpdateStatus` | Counts real queue states and distinguishes Ready, Degraded, and Failed tool health | Fully implemented |
| 6 | Paste | `DownloadsView.PasteClicked` | Reads clipboard and only fills the URL field | Fully implemented; it never analyzes or queues implicitly |
| 7 | URL validation | `UrlChanged` / paste validation | `YtDlpMetadataService.TryValidateSourceUrl` | Fully implemented; Analyze starts disabled and invalid pasted text gets feedback |
| 8 | Analyze | `AnalyzeClicked` | `YtDlpMetadataService` → `YtDlpProcessRunner` → real yt-dlp process | Fully implemented |
| 9 | Analysis cancellation/retry | subsequent Analyze cancels prior CTS | process cancellation kills the process tree | Fully implemented |
| 10 | Empty/analyzing/analyzed/error card states | `SetPreviewState` | Reflects actual operation result and adaptive per-state height | Fully implemented |
| 11 | Preview/queue/history thumbnails | views → `ThumbnailCacheService` | HTTP/cache/Skia decode; globally gated by setting; shared loads and view/service disposal are cancellation-safe | Fully implemented |
| 12 | Quality selector | metadata → `FormatSelector.GetAvailablePresets` | Advertised “up to” presets use the same selectable-range semantics as `FormatSelector.Select` | Fully implemented |
| 13 | Audio-only preset | quality selector → queue item | selects best audio-only format and downloads its native container | Fully implemented; it is extraction, not MP3 conversion |
| 14 | Details | `DetailsClicked` | `VideoDetailsForm.ShowDialog(owner)` with null-safe formatting | Fully implemented |
| 15 | Details tabs | Overview/Metadata/Formats/Subtitles/Chapters | renders normalized model collections and scrollable text | Fully implemented |
| 16 | Add to queue | `AddClicked` | format selection → `DownloadQueueItem.FromMetadata` → queue persistence | Fully implemented |
| 17 | Duplicate click protection | `AddClicked` | `DownloadQueueService.ContainsPendingOrActive` | Fully implemented after A-08; completed/failed/cancelled items may intentionally be added again |
| 18 | Queue pause/resume | toolbar button | `DownloadQueueService.SetPaused` and persisted `QueuePaused` state | Fully implemented after A-02; pause means do not start another item, not suspend active bytes |
| 19 | Automatic queue start | queue/settings signal | one bounded `QueueProcessor` admission loop → independently owned `DownloadJobExecutor` tasks | Fully implemented |
| 20 | Progress/speed/ETA | yt-dlp progress template | `YtDlpProgressParser` → queue updates → card | Fully implemented |
| 21 | Cancel active | card/menu action | per-item linked CTS, including deterministic Waiting-to-active handoff, → process-tree kill | Fully implemented |
| 22 | Retry failed/cancelled | card/menu action | resets terminal state and wakes processor | Fully implemented |
| 23 | Remove item | card/menu action | rejects active item and persists collection | Fully implemented |
| 24 | Clear pending | global menu | removes queued/waiting records and persists | Fully implemented |
| 25 | Move first/up/down/last | card/global menus | `Move`/`MoveBy`, persistence, UI rebuild | Fully implemented |
| 26 | Context-action enablement | `BuildQueueMenu` and per-card menu | state- and position-aware actions | Fully implemented after A-07 |
| 27 | Queue auto-scroll/wheel | `BuildQueueCards` / `QueueScrollPlanner` | ModernFormsNext master scrolling and calculated target | Fully implemented |
| 28 | Queue restart recovery | `QueuePersistenceService` / `LoadAsync` | active states become queued with reset transient progress | Fully implemented |
| 29 | Parallel download limit | Settings concurrency combo | live 1/2/3 bound controls scheduler admission; lowering never cancels running jobs | Fully implemented |
| 30 | Video stream download | queued format | yt-dlp `-f`, safe paths, progress and final-file marker | Fully implemented |
| 31 | Separate video/audio download | format selection | two yt-dlp calls followed by FFmpeg | Fully implemented |
| 32 | FFmpeg merge/remux | executor → `FfmpegService.MergeAsync` | direct process invocation with progress and no shell | Fully implemented |
| 33 | Output directory | Settings → `AppSettings` | write probe, final path resolution, move | Fully implemented |
| 34 | Temporary directory/cleanup | Settings → executor | per-attempt directories; successful cleanup or retention | Fully implemented |
| 35 | Filename template | Settings validation/save | passed directly as yt-dlp output template | Fully implemented |
| 36 | Rename conflict policy | Settings combo | `FileSystemUtilities.ResolveFinalPath` chooses numbered name | Fully implemented |
| 37 | Overwrite policy | checkbox/combo | final resolution and `File.Move(..., overwrite)` | Fully implemented |
| 38 | Fail conflict policy | Settings combo | throws before replacing existing file; queue becomes failed | Fully implemented |
| 39 | Internal metadata cache | checkbox | exact raw yt-dlp JSON written atomically under metadata directory | Fully implemented |
| 40 | JSON sidecar | checkbox | raw full JSON written beside media with same basename | Fully implemented |
| 41 | Publication-date CreationTime | checkbox | resolver handles exact timestamps and date-only values | Fully implemented |
| 42 | History persistence | successful finalization | `HistoryService.AddAsync` and atomic JSON save | Fully implemented |
| 43 | History search | text change | culture-insensitive match over title/channel/quality/path | Fully implemented |
| 44 | History open file/folder | card buttons | shell service; buttons disabled for missing targets | Fully implemented |
| 45 | Managed yt-dlp, FFmpeg/ffprobe, and Deno install/update | overlay/settings | official release sources, bounded download, checksum when supplied, Zip-Slip guard, validation, versioned activation | Fully implemented |
| 46 | Custom tool paths | checkboxes/browse/save | `ToolManager` validates custom yt-dlp, FFmpeg/ffprobe, and Deno executables and disables managed updates for them | Fully implemented |
| 47 | Custom yt-dlp arguments | multiline field/save | safe tokenizer, forbidden option/URL checks, applied to analysis and downloads | Fully implemented after A-03 |
| 48 | Theme Light/Dark/System | combo | `AppAppearanceService` applies semantic resources and persists mode | Fully implemented |
| 49 | Language PL/EN | combo | `LocalizationService`, immediate rebind, persisted restart choice, and localized queue-detail reason keys | Fully implemented |
| 50 | Save settings | button | validation → atomic JSON → tool reconfiguration | Fully implemented; duplicate async invocation blocked after A-09 |
| 51 | Open logs | button | opens real AppData log directory | Fully implemented |
| 52 | Global exception handling | application boundary | stack trace to file logger plus localized fatal message | Fully implemented |
| 53 | Graceful shutdown | form closing | cancels processor/tools and saves queue/settings/history before close | Fully implemented |
| 54 | Localization resource parity | all localized controls | 233 EN keys and 233 PL keys, no missing counterpart | Fully implemented |
| 55 | Per-capability engine readiness | preparation overlay / Analyze / queue | analysis leases yt-dlp+Deno; FFmpeg is required only by merge/remux jobs; aggregate state can be Degraded | Fully implemented |

Two additional fully implemented infrastructure groups counted in the headline are safe command argument transport through `ProcessStartInfo.ArgumentList` and sensitive command-log redaction. Tool version detection, update-while-in-use activation, offline installed-tool use, retry state, and stdout/stderr draining are consolidated into rows 4, 8/9, 20, 30, and 45. The originally partial and suspicious groups and their resolutions are detailed below.

## Settings end-to-end matrix

| Setting | UI | Persist/reload | Backend consumer | Status |
|---|---|---|---|---|
| `TemporaryDirectory` | textbox + browse | Yes | `DownloadJobExecutor` | OK |
| `FinalOutputDirectory` | textbox + browse | Yes | finalization and history | OK |
| `DefaultQualityPresetId` | combo | Yes | analyzed-card initial selection | OK |
| `MaxSimultaneousDownloads` | 1/2/3 combo | Yes | bounded queue admission scheduler | OK; applies live without terminating excess active jobs |
| `KeepTemporaryFilesAfterSuccessfulMerge` | checkbox | Yes | executor cleanup | OK |
| `UseCustomYtDlp`, path | checkbox + file picker | Yes | `ToolManager` | OK |
| `UseCustomFfmpeg`, paths | checkbox + file picker | Yes | `ToolManager` / `FfmpegService` | OK; ffprobe defaults to sibling executable |
| `UseCustomDeno`, path | checkbox + file picker | Yes | `ToolManager` / `YtDlpProcessRunner` | OK; version 2.3.0+ required and exact verified path is passed to yt-dlp |
| `FilenameTemplate` | textbox | Yes | yt-dlp download arguments | OK |
| `OverwriteExistingFiles` | checkbox | Yes | final path and move | OK |
| `ConflictBehavior` | combo | Yes | final path resolution | OK |
| `StoreMetadataJson` | checkbox | Yes | internal metadata cache | OK |
| `DownloadThumbnail` | checkbox | Yes | preview, queue, and history thumbnail service | OK after A-04 |
| `SetFileCreationTimeFromMediaPublishDate` | checkbox | Yes | post-processor | OK |
| `SaveMetadataJsonSidecar` | checkbox | Yes | post-processor | OK |
| `ThemeMode` | combo | Yes | appearance service | OK |
| `Language` | combo | Yes | localization service | OK |
| `CustomYtDlpArguments` | multiline textbox | Yes | analysis and downloads | OK after A-03 |
| `QueuePaused` | toolbar state, not a settings row | Yes | queue scheduler gate | OK after A-02 |

Missing settings files use defaults. Syntactically invalid JSON is logged and replaced with defaults. Semantically damaged values are now normalized individually, so a null language/path or unknown enum no longer discards otherwise valid preferences.

## Fixes completed during the audit

| ID | Priority | Feature/location | Previous actual behavior and cause | Expected behavior | Fix |
|---|---|---|---|---|---|
| A-01 | P1 | Build dependency / CI | Local checkout and workflows were pinned behind current MFN master; the remote advanced again during the audit and once during final validation | Development, CI, and release must use one current source revision | Clean worktree, `Directory.Build.props`, README, CI, and release workflow now pin `6e3a7cf...` |
| A-02 | P1 | Queue Pause | `isPaused` existed only in memory; restart silently resumed pending work | A deliberately paused queue must stay paused across restart | Persisted `QueuePaused` via `SettingsService`; startup initializes scheduler state from it |
| A-03 | P1 | Advanced yt-dlp arguments | `YtDlpDownloadService` used the setting, but metadata analysis ignored it | Authentication/site options must affect the operation that needs them, including analysis | Shared safe tokenization is now used by metadata argument construction before `-- URL` |
| A-04 | P2 | Download thumbnails | Only the analyzed preview checked the setting; queue/history still downloaded/cache thumbnails | OFF must stop every application thumbnail fetch | Central gate added to `ThumbnailCacheService` and wired to live settings |
| A-05 | P1 | Startup persistence | A `null` record in otherwise valid queue/history JSON could be dereferenced during startup | Partial corruption should be logged/ignored without terminating startup | Null records are filtered and loaded records are normalized |
| A-06 | P1 | Semantic settings corruption | Null `Language` caused an exception and reset every preference; unknown enum/null paths survived poorly | Invalid fields should fall back individually | Added enum, language, directory, preset, template, and custom-argument normalization |
| A-07 | P2 | Global queue menu | All selected-item actions were enabled solely because a selection existed, even when backend would no-op | Enabled state must match status and position | Menu now derives move/retry/remove/cancel/clear availability from the selected record and index |
| A-08 | P2 | Add to queue | Repeated clicks added unlimited identical pending jobs | One nonterminal media+quality job should not be duplicated accidentally | Atomic snapshot guard plus localized feedback; terminal jobs remain re-downloadable |
| A-09 | P2 | Settings async actions | Save/update/retry buttons stayed enabled during awaited work; update failure could still show completion | Prevent duplicate operations and show attention on per-tool failure | Shared busy gate and status-aware feedback added |
| A-10 | P2 | Analyze initial state/paste | Analyze was initially enabled until the first text-change event; invalid pasted text had no direct feedback | Invalid/empty state must not launch analysis | Button now starts disabled and invalid paste shows localized feedback |
| A-11 | P2 | Light selected navigation | White selected text/icon on `#D7E2FF` was visibly low contrast | Selected text/icon need readable semantic color in every theme | Added `NavigationSelectedText`; Light uses primary text, Dark retains white; rendering test enforces at least 4.5:1 |
| A-12 | P2 | Process log privacy | Inline `--username=value` could be written to logs | Credentials must be redacted in separate and inline forms | Inline username redaction plus regression coverage added |
| A-13 | P2 | Tool-manager regression stability | A reuse test kept the first `ToolManager` active while opening a second manager on the same state directory, allowing background lifecycle work to race its zero-call assertion | Reuse verification must have one active owner of the persisted tool state at a time | The first manager is now stopped before the second manager starts; cleanup ownership remains unchanged |
| A-14 | P1 | Concurrent download limit | The persisted 1/2/3 selector had no backend consumer | Bound actual starts, support live limit changes, and isolate item ownership | Replaced the single-active worker with one controlled admission loop and per-item task/CTS ownership; 1/2/3 and 3→1→3 are regression-tested |
| A-15 | P1 | yt-dlp JavaScript runtime | No supported runtime was managed or routed to current yt-dlp | Provide a verified runtime without relying on global PATH | Added managed/custom Deno 2.3.0+ provisioning, validation, update/reuse, UI state, and explicit `--js-runtimes deno:<path>` injection |
| A-16 | P2 | Sparse quality ladders | A bounded preset was hidden unless the source had an exact matching height | “Up to” availability must match selection semantics | `GetAvailablePresets` now uses the same selectability rule as `Select`; sparse ladders are tested |
| A-17 | P2 | Waiting cancellation | A click between queue claim and active CTS publication could be lost | An enabled Cancel must be deterministic | Pre-registration cancellation intent is retained and applied as soon as per-item ownership is registered |
| A-18 | P2 | Queue detail localization | Persisted English transition strings leaked into PL UI | Present semantic localized state while preserving useful diagnostics | Added compatible `StatusMessageKey`, migration inference, EN/PL keys, and localized Details rendering |
| A-19 | P2 | Thumbnail lifetime | A shared load or late completion could outlive a caller/view/service | Cancellation must be caller-safe and no UI/cache insertion may occur after disposal | Shared loads use service lifetime; callers wait independently; views/cards guard callbacks with lifetime tokens |
| A-20 | P2 | Tool health semantics | An update failure on a still-valid binary and a missing capability could be shown ambiguously | Separate operational-with-warning from unusable | Added `DownloadEngineStatus.Degraded` and capability-derived status/feedback |
| A-21 | P2 | FFmpeg-independent analysis | Aggregate readiness blocked analysis and combined streams when only FFmpeg was absent | Require tools at the operation boundary | Analysis leases yt-dlp+Deno; merge/remux alone leases FFmpeg; missing FFmpeg yields explicit degraded capability |
| A-22 | P3 | Unused locale entries | Eight static keys had no call site | Remove only confirmed dead resources | Removed the eight confirmed-unused EN/PL entries; parity remains tested |
| A-23 | P3 | Process callback diagnostics | Line-callback exceptions were swallowed silently | Keep draining pipes but expose the first callback defect | Logs one callback exception per stream without rethrowing into the drain loop |
| A-24 | P3 | Retry diagnostics | Retry retained stale `TemporaryFiles` and `FinalFile` | Current-attempt fields must describe the new attempt | Retry clears both fields along with other transient state |
| A-25 | P3 | Thumbnail placeholders | Queue/history/preview used `MTD` or `URL` fallback text | Use the existing central visual branding | Replaced text placeholders with `AppBranding` vector/icon fallback; no asset duplication |
| A-26 | P1 | Concurrent metadata finalization | Simultaneous jobs for the same video could race two atomic moves to one metadata path | Concurrent completion must never corrupt or lose internal metadata | Serialized atomic JSON writes and added a 24-writer same-destination regression test |

## ⚠️ Partially implemented

No audited group remains partially implemented. F-01 through F-08 were resolved by A-14 through A-21. The compatibility boundary is preserved: old queue JSON without `StatusMessageKey`, existing managed tool state without Deno fields, and installations using custom yt-dlp/FFmpeg paths continue to load; Deno is added as an independently validated capability.

## ❌ Not implemented / broken

No remaining user-exposed feature was classified as completely broken after A-01 through A-26. This does not mean that every media site, private/age-restricted video, live/premiere date, or network failure was exercised live. Those are validation coverage limits, not evidence of success.

Not exposed at the 2026-09-08 audit date and therefore not counted as broken: playlist expansion, batch/multiple URLs, subtitle downloading, metadata embedding into media, transcoding/audio format conversion, notification toasts, clear-completed, and history deletion. Playlist expansion has since been added locally; see the addendum above.

## 🧹 Dead / obsolete / suspicious code

The four investigated groups are resolved with narrow changes: S-01's eight confirmed-unused locale keys were removed; S-02 logs the first callback failure per process stream while continuing to drain; S-03 clears stale attempt file fields on retry; S-04 uses the central branding fallback instead of `MTD`/`URL` text. Protocol identifiers such as `MTD_PROGRESS` and `MTD_FILE` remain because they are active machine-readable contracts, not obsolete branding placeholders.

## yt-dlp, Deno, and FFmpeg implementation audit

- Executables are never invoked through a shell. `ProcessStartInfo.ArgumentList` preserves spaces and Unicode without manual command-line quoting.
- At the audit date, yt-dlp always received `--ignore-config`, `--no-playlist`, stable UTF-8 and progress/output markers, and `--` before the URL. The new explicit-playlist metadata path uses `--yes-playlist --flat-playlist` while single-video analysis/download still uses `--no-playlist`. Custom arguments remain tokenized and cannot override application-owned routing or inject another URL.
- The official PyInstaller `yt-dlp.exe` already contains `yt-dlp-ejs`; no separate script package is copied. ModernTubeDownloader provisions Deno from the official `denoland/deno` release, validates version 2.3.0+, leases it with yt-dlp, and prepends `--js-runtimes deno:<verified-executable-path>` to both analysis and download commands.
- stdout and stderr are drained concurrently. Cancellation kills the complete process tree. Nonzero yt-dlp and FFmpeg exits become typed exceptions and queue failures.
- yt-dlp analysis deserializes structured JSON and preserves the full raw JSON for sidecar/cache use. Unknown JSON fields survive because raw JSON is retained.
- FFmpeg is invoked only by the application's merge/remux path with stream copy; there is no claimed transcoder. FFprobe is detected/validated as part of the FFmpeg installation but is not needed for the current merge implementation.
- Managed sources are the GitHub latest-release APIs for `yt-dlp/yt-dlp`, `yt-dlp/FFmpeg-Builds`, and `denoland/deno`; the selected FFmpeg asset is explicitly the GPL build. Installation uses bounded downloads, checksum verification when supplied, archive traversal/expanded-size guards, staging, executable validation, and versioned activation.
- An update while a tool is leased is staged and activated when usage reaches zero. Failed updates keep the last installed version rather than replacing it with an unverified binary.
- Sensitive separate and inline credential forms are redacted from process command logs. Media URLs are rendered only as `<http-url>`/`<https-url>`.

## Queue state and race audit

- Collection mutations and snapshots are synchronized; persistence writes are serialized and atomic. Concurrent internal-metadata/sidecar JSON writes are also serialized, including the same-video completion case exposed by the three-job UI smoke.
- One event-driven admission loop starts no more than the live 1/2/3 limit and owns a separate task and linked CTS per item. Lowering the limit leaves running jobs alone and blocks new admission until the active count is below the new limit; raising it wakes admission immediately.
- Queue pause persists and blocks only new admission; it does not suspend bytes already handled by yt-dlp or FFmpeg. A failure or cancellation is contained to its item and the scheduler keeps advancing other work.
- Shutdown cancels every active linked token, kills each corresponding process tree, waits for the scheduler and all item tasks, and saves queue/settings/history.
- Interrupted Waiting/Downloading/Merging/Finalizing items are recovered as Queued with transient metrics cleared.
- Active items cannot be removed/reordered. Failed/cancelled items can retry. Pending items can be cleared. Menu enabled state now mirrors those rules.
- The claim-to-registration cancellation boundary is deterministic: cancellation requested while an item is `Waiting` is retained until its CTS exists. Tests cover no double-start, limits 1/2/3, dynamic 3→1→3, failure/cancel isolation, pause, shutdown, and the Waiting handoff.

## Localization and UI-state audit

- EN and PL contain 233 matching keys. Missing-key fallback is English, then `[key]`.
- Language changes immediately rebuild labels/options/cards and persists for restart.
- Theme changes use semantic tokens. Navigation normal/hover/focus/pressed/selected styles set both background and foreground; selected Light contrast now has a 4.5:1 regression assertion.
- Empty/analyzing/analyzed/error preview states have distinct compact heights. Queue/history cards switch height and action layout at narrow widths. Settings and Details use framework scrolling rather than wheel forwarding workarounds.
- Analyze, quality, Details, and queue action enablement follow current state. Settings save/update/retry now block duplicate async execution.
- Queue status summaries use stable localization keys with backward inference for existing persisted records; raw technical errors remain available where useful. A complete physical 125%/150% DPI matrix was not executed in this audit.

## Validation results

| Check | Result |
|---|---|
| ModernFormsNext source | clean detached HEAD = `origin/master` = `6e3a7cfd148915f51b305aa17296d41fbd9222c1` at final validation |
| `dotnet restore` | Passed |
| Debug build | Passed, 0 warnings, 0 errors |
| Release build | Passed, 0 warnings, 0 errors |
| Full Release tests | Passed: 94/94, 0 failed, 0 skipped |
| Regression additions | previous audit regressions plus scheduler limits 1/2/3, dynamic limit changes, no double-start, item failure/cancel isolation, pause/shutdown, Waiting handoff, Deno provisioning/custom/reuse/version/routing, degraded FFmpeg capability, sparse quality ladders, status localization, thumbnail lifetimes, and concurrent atomic JSON writes |
| Current-process smoke | concurrency interaction smoke: PL/Dark, explicit Deno routing, paused three-item queue, Resume to three active jobs, independent progress/different completion paths, later active-item cancel, and Deno Settings state; final MFN `6e3a7cf...` smoke: startup, Ready state, persisted queue render, and clean close |
| PL/EN | Both observed in this audit; 233/233 automated resource parity |
| Light/Dark/System | Light and Dark observed; System mapping covered by style/appearance tests |
| Details | all five tabs, scroll, resize, close/reopen observed earlier in this audit; construction/null-safety regressions passed on final MFN; the dependency-only final startup smoke did not reopen Details |
| Queue UI | 22-item auto-scroll/wheel observed earlier; current smoke observed three concurrent jobs and one manual active-item cancellation; no runtime data was committed |
| Live managed Deno provisioning | NOT EXECUTED; download/checksum/validation/activation/reuse/update are deterministic tests, while the UI smoke used an explicitly configured fake Deno executable |
| Current live Internet download | NOT EXECUTED on 2026-09-08; deterministic process E2E passed, older live evidence is dated 2026-09-04 |
| Playlist/multiple URL/subtitle download/transcode | At audit date NOT EXPOSED; explicit playlist URLs added in the 2026-09-20 local addendum, other items unchanged |

One full test pass initially produced a Windows temporary-directory access failure in `ToolManagerTests.UpdateWhileToolIsBusy_IsActivatedAfterLeaseEnds`; the same test passed immediately in isolation. A later run exposed concurrent ownership in `ExistingValidInstall_IsReusedWithoutDownload`; A-13 removed that invalid overlap. The three-job UI smoke then exposed a real same-destination internal-metadata write race; A-26 fixed it instead of treating the resulting completion warning as test noise. The final clean validation pass is the result recorded above.

## Repository hygiene

No commit, push, tag, release, package publication, source deletion, reset, stash, or clean was performed. The audit preserved the pre-existing uncommitted sidebar branding/hover work in `MainForm.cs`, `AppBranding.cs`, `AppUi.cs`, and `SidebarNavigationStyleTests.cs`. Test profiles, screenshots, downloaded tools, and helper scripts stayed outside the repository and are not staged.
