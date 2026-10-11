# yt-dlp feature gap audit — candidate.5, 2026-10-10

## Executive summary

This is an audit and roadmap, **not a proposal to expose all of yt-dlp in the GUI**. No converter, proxy UI, archive system or bulk feature settings were implemented in this round.

MTD already implements its own persistent queue/retries/pacing, playlist selection, compatible format/container selection, ranges, subtitle sidecars/embedding, publication timestamps, JSON sidecars, separate LIVE scheduling/recovery, managed tools and source catalog. These are not missing simply because MTD does not expose every equivalent CLI switch. Custom arguments are an expert escape hatch, **not tested first-class feature support**.

The new cookie path covers extraction and media requests centrally, retains one jar within an attempt and takes newer snapshots at retry/LIVE boundaries. The remaining **P0 gap** is desktop-initiated browser refresh after an authentication failure: the one-shot Native Messaging protocol does not implement a reverse request/ACK channel. Never report that the browser refreshed when it did not. See [cookie architecture](COOKIE_SYNC.md) and [qualification report](CANDIDATE5_COOKIE_SYNC_YTDLP_AUDIT_REPORT.md).

SponsorBlock Remove remains Experimental with owner-confirmed playback defects. Do not classify an upstream option or passing model test as verified A/V quality. Active LIVE recording is FromNow; FromStart is experimental/source-dependent, not a stable guarantee.

## Exact evidence and method

- Managed Windows binary: **2026.08.19**; SHA-256 `66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a`.
- Current upstream master captured: `51bab8a0116f4d8004c315706d809782607d5847`, commit timestamp `2026-09-27T22:09:13Z`.
- Inventory capture: `2026-10-10T15:12:12.6707350Z`, actual `--version` and `--help`: **255 syntax rows, 250 distinct long options, 17 categories**.
- Reproducible tool: [Get-YtDlpInventory.ps1](scripts/Get-YtDlpInventory.ps1). Raw help/options/evidence remain in ignored `artifacts/validation/candidate5/yt-dlp-inventory/`, not a runtime capability service.
- Upstream comparison: [README](https://github.com/yt-dlp/yt-dlp/blob/51bab8a0116f4d8004c315706d809782607d5847/README.md), [options.py](https://github.com/yt-dlp/yt-dlp/blob/51bab8a0116f4d8004c315706d809782607d5847/yt_dlp/options.py), [cookies.py](https://github.com/yt-dlp/yt-dlp/blob/51bab8a0116f4d8004c315706d809782607d5847/yt_dlp/cookies.py). Managed version and upstream SHA are deliberately distinct; this audit did not update the installed tool or claim it is the latest release.

Rows below group related positive/negative options to avoid a 250-row wall. Every distinct long option in the captured help is named. Status counts refer to **grouped feature rows**, not individual switches. Hidden/deprecated options are not claimed exhaustively covered by visible help. In particular legacy playlist-start/end/reverse and views filters are considered under playlist/filter/extractor arguments; they are not separate current-help rows. No hidden flags are proposed as supported GUI contracts.

Mechanically checked: **100 grouped rows; 250/250 option names covered; 0 unnamed options**.

| Status | Grouped rows |
|---|---:|
| A Implemented | 10 |
| B Partial | 22 |
| C Backend only | 3 |
| D UI only/problem | 0 |
| E Missing/high value | 13 |
| F Missing/low value | 8 |
| G Expert only | 18 |
| H Intentionally not exposed | 8 |
| I Unsafe/not appropriate | 13 |
| J Upstream/source dependent | 5 |

### Status and evidence legend

A IMPLEMENTED; B PARTIAL; C BACKEND ONLY; D UI ONLY/problem; E MISSING/HIGH VALUE; F MISSING/LOW VALUE; G EXPERT ONLY; H INTENTIONALLY NOT EXPOSED; I UNSAFE/NOT APPROPRIATE; J UPSTREAM/SOURCE DEPENDENT.

`—` means no dedicated UI/model/test. `Args` means guarded CustomYtDlpArguments persistence, not a typed model or functional qualification. A negative option name in a row does not mean both are configurable: defaults/ownership are stated in Notes. `Yes` in Source dependent means formats, protocol, server or extractor behavior can limit the result.

Code evidence identifiers (repository-relative):

- **T**: `Services/ToolManager.cs`, `Services/ToolProvisioning.cs`, `Settings/AppSettings.cs`; `CoreTests` / `ToolManagerTests`.
- **M**: `Services/YtDlpMetadataService.cs`, `Models/VideoMetadata.cs`, `Views/VideoDetailsForm.cs`; `CoreTests`, `WorkflowIntegrationTests`, `PlaylistTests`, `FormatSelectionStabilizationTests`.
- **Q**: `Services/DownloadQueueService.cs`, `Services/QueueProcessor.cs`, `Services/DownloadJobExecutor.cs`, `Settings/AppSettings.cs`; `ResilienceTests`, `WorkflowIntegrationTests`, `PlaylistTests`.
- **F**: `Services/FormatSelector.cs`, `Services/FfmpegService.cs`, `Models/QualityPreset.cs`; `CoreTests`, `FormatSelectionStabilizationTests`.
- **D**: `Services/YtDlpDownloadService.cs`, `Services/YtDlpProcessRunner.cs`, workspace ownership in `Services/DownloadJobExecutor.cs`; `CoreTests`, `ResilienceTests`.
- **P**: `Services/DownloadPostProcessor.cs`, `Services/MediaPublishTimeResolver.cs`, filename/conflict utilities; `PostProcessingTests`.
- **S**: `Services/SubtitleDownloadService.cs`, `Models/DownloadEnhancements.cs`, `Services/DownloadOptionCompatibilityValidator.cs`; `EnhancementTests`, `MediaTimeRangeTests`.
- **L**: `Services/LiveRecordingExecutor.cs`, `Services/LiveAvailabilityProbe.cs`, `Services/LiveCapturePolicy.cs`, LIVE service/scheduler/store; `LiveSubsystemTests`, `ResilienceTests`.
- **C**: `Authentication/CookieContracts.cs`, `CookieStore.cs`, `NativeBridge.cs` (under `ModernTubeDownloader.Authentication`), `Services/YtDlpAuthenticationProvider.cs`, `Views/CookieSyncSettingsPanel.cs`; `CookieAuthenticationTests` and `browser-extension/tests/cookies.test.cjs`.
- **X**: `Utilities/CommandLineArgumentTokenizer.cs`, `Settings/AppSettings.cs`, `Views/SettingsView.cs`; `CoreTests`, `CookieAuthenticationTests`. Pass-through acceptance is not a CLI behavior test.
- **R**: `Services/SupportedSourcesService.cs`, `Services/SourceCheckService.cs`; `SupportedSourcesTests`, source/automation tests.

Tests column cites a suite/code group covering MTD's stated behavior, **not every CLI alternative in that row**. Security column is a risk/ownership note, not a declaration that arbitrary upstream features are safe.

Short test labels: `ResilienceTests` = `DownloadResilienceTests`, `PostProcessingTests` = `DownloadPostProcessingTests`, `EnhancementTests` = `DownloadEnhancementTests`; other suite labels match their actual filenames under `ModernTubeDownloader.Tests/`.

## General Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| General | --help, --version | C | Tool version | T | Tool manifest | T | No | No auth | P3 | Catalog/version calls need no source cookie jar; help is audit-only. |
| General | --update, --no-update, --update-to | B | Tool update/override | T | Settings + installed versions | T | No | Verified download | P1 | MTD owns update/rollback/leases rather than letting running jobs self-update. Arbitrary channel selection is not UI. |
| General | --ignore-errors, --no-abort-on-error, --abort-on-error | B | Queue failure/retry | Q,M | Jobs/defaults | Q,M | Yes | Explicit failure | P1 | Flat playlist ignores unavailable entries; ordinary jobs fail visibly; CLI error policy is reserved. |
| General | --list-extractors, --extractor-descriptions | A | Supported services | R | Catalog cache | R | Yes | No private metadata | P2 | Runtime catalog, not universal source qualification. |
| General | --use-extractors, --default-search | G | Custom args only | X | Args | X | Yes | Source ambiguity | P3 | No search-query UI or automatic channel expansion. |
| General | --ignore-config, --no-config-locations, --config-locations | H | — | D fixed ignore-config | — | D,X | No | Prevent hidden configuration | WONTFIX | Arbitrary config locations blocked; system config cannot override owned auth/output. |
| General | --plugin-dirs, --no-plugin-dirs | I | No plugin loader | X guard | — | C | Yes | Arbitrary code | WONTFIX | Positive plugin paths blocked. No plugin discovery or isolation claim. |
| General | --js-runtimes, --no-js-runtimes | B | Managed/custom Deno | D,T | Tool settings | T,D | Yes | Trusted tool path | P2 | Explicit verified Deno; multi-runtime configuration is not first-class. |
| General | --remote-components, --no-remote-components | I | — | X guard positive option | — | C | Yes | Remote code | WONTFIX | Do not use remote components to bypass the managed-tool trust boundary. |
| General | --flat-playlist, --no-flat-playlist | A | Playlist preview | M,Q | Selected jobs | M,Q | Yes | Bounded metadata | P1 | Flat preview then per-entry real analysis; reserved CLI switches. |
| General | --live-from-start, --no-live-from-start, --wait-for-video, --no-wait-for-video | B | Separate LIVE | L | LIVE sessions/parts | L | Yes | Explicit capture | P0 | Existing waiting/polling/reconnect is implemented; FromStart remains experimental. |
| General | --mark-watched, --no-mark-watched | I | — | X guard positive option | — | C | Yes | Account mutation | WONTFIX | Downloading must not silently mutate watch history. |
| General | --color, --compat-options | G | Custom args only | X | Args | X | Yes | Diagnostic/parser compatibility | P3 | Compatibility flags can alter semantics; not certified presets. |
| General | --alias, --preset-alias | I | — | X guard | — | C | No | Argument ownership bypass | WONTFIX | Aliases/presets can reintroduce reserved credentials/output/commands. |

## Network Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Network | --proxy | E | No typed proxy settings | X pass-through | Args | X only | Yes | Proxy credentials/traffic | P2 | Future explicit profile; do not persist passwords in raw arguments. |
| Network | --socket-timeout | G | Custom args only | X; bounded MTD metadata checks | Args | R,Q | Yes | Bounds | P2 | MTD timeout/cancellation already exists; not a missing overall timeout system. |
| Network | --source-address, --force-ipv4, --force-ipv6 | G | Custom args only | X | Args | X only | Yes | Network binding | P3 | No network-selection UI. |
| Network | --impersonate, --list-impersonate-targets | J | — | Depends on tool build | Args if accepted | — | Yes | TLS/backend dependencies | P3 | Inventory does not prove the packaged binary contains every impersonation backend. |
| Network | --enable-file-urls | I | — | X + input URL guard | — | C, external-link tests | No | Local file access | WONTFIX | App handoff only accepts safe HTTP(S). |

## Geo-restriction

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Geo | --geo-verification-proxy, --xff | G | Custom args only | X | Args | X only | Yes | Traffic/routing | P3 | No geographic availability guarantee or bypass recommendation. |

## Video Selection

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Selection | --playlist-items | B | Individual paged selection | M,Q | Selected queue items | PlaylistTests | Yes | Bounded selection | P1 | Equivalent selected items; no CLI index-expression/reverse/random UI. |
| Selection | --min-filesize, --max-filesize | G | Custom args only | X | Args | X only | Yes | Approximate source size | P2 | Not typed pre-admission limits. |
| Selection | --date, --datebefore, --dateafter | E | — | X pass-through | Args | X only | Yes | Metadata validity | P1 | Publication CreationTime exists, not a date-filter UI. |
| Selection | --match-filters, --no-match-filters, --break-match-filters, --no-break-match-filters | G | Custom args only | X | Args | X only | Yes | Untrusted expression/diagnostics | P3 | No validated views/duration/custom filter model. |
| Selection | --no-playlist, --yes-playlist | A | Explicit playlist analysis | M,Q | Request/jobs | PlaylistTests | Yes | No accidental bulk capture | P1 | Watch+list stays single video; reserved ownership. |
| Selection | --age-limit | G | Custom args only | X | Args | X only | Yes | Access/age policy | P3 | Does not provide login or authorize restricted content. |
| Selection | --download-archive, --no-download-archive | E | — | X pass-through | Args, not app archive | X only | Yes | Private viewing history | P1 | Current nonterminal queue duplicate checks/history are not a durable yt-dlp archive. |
| Selection | --max-downloads, --break-on-existing, --no-break-on-existing, --break-per-input, --no-break-per-input, --skip-playlist-after-errors | B | Selection + queue failure policy | Q + X | Jobs/Args | Q | Yes | Predictable batch state | P2 | MTD explicit item queue handles per-job errors; no typed CLI batch stop policies. |

## Download Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Download | --concurrent-fragments | E | — | X pass-through | Args | X only | Yes | Server load | P1 | Existing 1–3 concurrent jobs is independent of fragment parallelism. |
| Download | --limit-rate, --throttled-rate | E | — | X pass-through | Args | X only | Yes | Bandwidth/false throttle | P1 | No typed bandwidth budget; admission pacing is already implemented. |
| Download | --retries, --file-access-retries, --fragment-retries, --retry-sleep | B | Bounded app retries | Q,D | Retry/pacing settings | ResilienceTests | Yes | No infinite retry | P0 | Reanalysis at each app attempt; inner yt-dlp retries are not unified UI. Auth-refresh gap remains. |
| Download | --skip-unavailable-fragments, --abort-on-unavailable-fragments | J | LIVE Partial/recovery | L + upstream | LIVE state/parts | L | Yes | Integrity vs availability | P2 | Recording verification is not proof every lost fragment can be recovered. |
| Download | --keep-fragments, --no-keep-fragments | G | Custom args only | X + workspaces | Args | D only | Yes | Disk growth | P3 | Workspace recovery is present; fragment retention policy is not typed. |
| Download | --buffer-size, --resize-buffer, --no-resize-buffer, --http-chunk-size | G | Custom args only | X | Args | X only | Yes | Memory/server behavior | P3 | Keep upstream defaults unless explicitly tuned. |
| Download | --playlist-random, --lazy-playlist, --no-lazy-playlist | F | — | X pass-through | Args | X only | Yes | Ordering | P3 | MTD paged preview + ordered jobs is deliberately different. |
| Download | --hls-use-mpegts, --no-hls-use-mpegts | B | LIVE/recovery, not CLI switch | L,D | LIVE/workspace | L | Yes | Playability | P2 | Existing live invocation policy; no stable all-protocol guarantee. |
| Download | --download-sections | A | Range editor desktop/remote | D,S | Per job/history | MediaTimeRangeTests | Yes | Compatibility validation | P1 | Same range to selected streams; no silent full download fallback; stream-copy keyframe accuracy. |
| Download | --downloader, --downloader-args | I | — | X guard | — | C | Yes | Arbitrary executable | WONTFIX | Tools are managed/custom validated executables, not arbitrary downstream command strings. |

## Filesystem Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Filesystem | --batch-file, --no-batch-file | F | URL/playlist UI | X pass-through | Args | X only | Yes | Local list access | P3 | No batch-file importer; existing multi-item queue is implemented. |
| Filesystem | --paths, --output, --output-na-placeholder | B | Output/temp/template | D,P | Settings | PostProcessingTests | Yes | Path traversal/escaping | P0 | Owned output/paths reserved. No per-output-type template UI or placeholder policy. |
| Filesystem | --restrict-filenames, --no-restrict-filenames, --windows-filenames, --no-windows-filenames, --trim-filenames | B | Output filename template | D,P | FilenameTemplate | P | Yes | Windows-safe paths | P0 | Windows filenames + trim are fixed backend policy, not all configurable pairs. |
| Filesystem | --no-overwrites, --force-overwrites, --no-force-overwrites | A | Rename/overwrite/fail | D,P | Conflict policy | PostProcessingTests | No | Explicit overwrite | P0 | Workspace/final move ownership; no arbitrary yt-dlp output override. |
| Filesystem | --continue, --no-continue, --part, --no-part | B | Resume interrupted VOD | Q,D | Resume/workspace/jobs | ResilienceTests | Yes | Matching identity/format | P1 | App workspace resumability exists; not universal upstream resume and no-part tuning. |
| Filesystem | --mtime, --no-mtime | B | Publish-date CreationTime | D,P | Timestamp setting | PostProcessingTests | Yes | Date provenance | P1 | MTD disables yt-dlp mtime and sets Windows CreationTime from publication metadata; not server LastWriteTime. |
| Filesystem | --write-description, --no-write-description | E | Details description only | M; X pass-through export | Args | M only | Yes | Personal source data | P2 | No dedicated description sidecar option. |
| Filesystem | --write-info-json, --no-write-info-json, --clean-info-json, --no-clean-info-json | B | JSON sidecar ON/OFF | M,P + credential scrubbing | Settings/job metadata | P,C | Yes | Credential/URL disclosure | P0 | App-generated same-basename JSON, not complete parity with all native info-json modes. |
| Filesystem | --write-playlist-metafiles, --no-write-playlist-metafiles | F | — | X pass-through | Args | — | Yes | Playlist/history disclosure | P3 | Entry jobs do not export a playlist manifest. |
| Filesystem | --write-comments, --no-write-comments | F | — | X pass-through | Args | — | Yes | Account/personal data | P3 | Not a supported comment-export feature. |
| Filesystem | --load-info-json | I | — | X guard | — | X | Yes | Arbitrary archived request data | WONTFIX | Queue recovery uses validated own contracts, not external raw metadata as process instructions. |
| Filesystem | --cookies, --no-cookies | B | Exclusive None/Manual/Synced | C central runner | DPAPI + source/path | C | Yes | Credentials, ACL, redaction | P0 | Real selected-domain ACK/parser confirmed; auth-failure reverse refresh still missing. |
| Filesystem | --cookies-from-browser, --no-cookies-from-browser | H | Extension opt-in instead | X guard + C | DPAPI | C | Yes | Browser DB/full-account export | WONTFIX | Desktop does not read browser databases; never mix two credential owners. |
| Filesystem | --cache-dir, --no-cache-dir, --rm-cache-dir | G | Own app cache paths | X; existing app caches | Args | Cache tests, not CLI modes | Yes | Paths/private cache | P3 | Thumbnail/catalog/app cache is not yt-dlp cache parity. |

## Thumbnail Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Thumbnail | --write-thumbnail, --no-write-thumbnail, --write-all-thumbnails, --list-thumbnails | E | Preview/notification image | M + ThumbnailCacheService | Cache | ThumbnailCacheServiceTests | Yes | URLs/size bounds | P2 | Existing DownloadThumbnail means app cache/display, not media thumbnail export. |

## Internet Shortcut Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Shortcut | --write-link, --write-url-link, --write-webloc-link, --write-desktop-link | F | — | X pass-through | Args | — | No | Local clickable files | P3 | No shortcut exporter. |

## Verbosity and Simulation Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Diagnostics | --quiet, --no-quiet, --no-warnings | G | Existing app log levels | X + D log projection | Args/log files | D,C | No | Keep actionable errors | P3 | Silencing upstream output is not recommended as a repair. |
| Diagnostics | --simulate, --no-simulate, --skip-download | C | Analyze/source check | M,R,S | Metadata/jobs | M,R,S | Yes | Metadata-only boundary | P1 | Internal paths own simulation; custom simulate/no-simulate blocked. |
| Diagnostics | --ignore-no-formats-error, --no-ignore-no-formats-error | J | Availability errors | M + upstream | Metadata | M,F,L | Yes | Do not fake success | P2 | UI cannot make absent formats downloadable. |
| Diagnostics | --print, --print-to-file, --dump-json, --dump-single-json | C | Details/progress/results | M,D | Metadata/history | M,D,C | Yes | Credential scrub/output ownership | P0 | Internal JSON/after-move marker. Custom output overrides blocked. |
| Diagnostics | --force-write-archive | F | — | X pass-through | Args | — | Yes | Archive truthfulness | P3 | Future archive must distinguish successful download from simulation. |
| Diagnostics | --newline, --no-progress, --progress, --console-title, --progress-template, --progress-delta | B | Native progress | D | Job state | CoreTests, ResilienceTests | No | Parser stability | P0 | Structured progress/newline/delta controlled internally; no console in GUI release. |
| Diagnostics | --verbose, --dump-pages, --write-pages, --print-traffic | I | — | X guard | — | C | Yes | Credential/raw response leakage | WONTFIX | Debug capture of authenticated traffic must not be an ordinary custom option. |

## Workarounds

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Workaround | --encoding, --bidi-workaround | G | PL/EN/Unicode app | X | Args | Localization tests, not CLI modes | Yes | Text parsing | P3 | App Unicode does not imply all upstream encoding modes tested. |
| Workaround | --legacy-server-connect, --no-check-certificates, --prefer-insecure | I | No promoted setting | X may accept | Args | No network qualification | Yes | TLS weakening | P0 | Do not recommend or expose in normal GUI. Guard hardening remains a follow-up; no universal safety claim for custom args. |
| Workaround | --add-headers | G | Custom noncredential headers | X; credential names blocked | Args | C | Yes | Cookie/Authorization injection | P0 | Abbreviations/inline forms checked; unrestricted raw headers are not an auth mechanism. |
| Workaround | --sleep-requests, --sleep-interval, --max-sleep-interval, --sleep-subtitles | B | Admission pacing/retry delay | Q + X | Settings/Args | ResilienceTests | Yes | Bounds/rate sensitivity | P2 | App pacing exists; request/subtitle sleeps are distinct expert controls. |

## Video Format Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Format | --format | A | Available quality/audio only | F,D | Quality/selection | F | Yes | No hidden format fallback | P1 | Exact source IDs resolved centrally; custom -f reserved. Audio only is not an MP3 converter. |
| Format | --format-sort, --format-sort-reset, --format-sort-force, --no-format-sort-force | E | Height-based quality | F current ordering; X tuning | Quality/Args | F only | Yes | Codec/HDR/container expectations | P1 | MTD height→fps→bitrate→size and codec-compatible selection exists; no explicit HDR/FPS/codec preference UI. |
| Format | --video-multistreams, --no-video-multistreams, --audio-multistreams, --no-audio-multistreams | H | One video/one audio | F fixed stream maps | Selection | F | Yes | Preserve expected streams | WONTFIX | Multiple languages/angles need a new tested selection/merge contract, not a casual checkbox. |
| Format | --prefer-free-formats, --no-prefer-free-formats | G | Custom args only | X; explicit containers F | Args | F only | Yes | Codec tradeoffs | P3 | No global preference UI. |
| Format | --check-formats, --check-all-formats, --no-check-formats, --list-formats | B | Actual formats/Details | M,F + local output probe | Metadata/selection | F,M | Yes | Bounded network probing | P2 | Discovery/output verification exists, not every CLI format-probe mode. |
| Format | --merge-output-format | A | Auto/MP4/MKV/WebM | F,D | Container | F | Yes | Stream-copy compatibility | P1 | Own FFmpeg stream-copy path; no silent re-encoding. |

## Subtitle Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Subtitle | --write-subs, --no-write-subs, --write-auto-subs, --no-write-auto-subs | A | Manual/auto ON/OFF | S | Defaults/per job | EnhancementTests | Yes | Same auth/session | P1 | Absent tracks warn without losing media; active LIVE exclusions explicit. |
| Subtitle | --list-subs, --sub-format, --sub-langs | B | Available languages/format | M,S | SubtitleOptions | EnhancementTests | Yes | Bounded codes | P1 | Language choices/Auto,SRT,VTT,ASS supported; not all regex/all-language syntax is GUI. |

## Authentication Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Auth | --username, --password, --twofactor | H | — | X guard | — | C | Yes | Account secrets/MFA | WONTFIX | Use authorized selected cookies/manual file; do not ask UI to collect account passwords. |
| Auth | --netrc, --netrc-location, --netrc-cmd | I | — | X guard | — | C | Yes | Credentials/command execution | WONTFIX | Shell/netrc path cannot bypass canonical credential policy. |
| Auth | --video-password | E | — | X currently blocks raw secret | — | C | Yes | Secret storage | P2 | High-value for some sources, but needs scoped secret UI/vault/lifetime, not arbitrary args. |
| Auth | --ap-mso, --ap-username, --ap-password, --ap-list-mso | H | — | X blocks credential variants | — | C | Yes | Provider account data | WONTFIX | Niche source auth not a universal GUI feature. |
| Auth | --client-certificate, --client-certificate-key, --client-certificate-password | I | — | X guard | — | C | Yes | Private keys/passwords | WONTFIX | No generic certificate credential importer. |

## Post-Processing Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Post-process | --extract-audio, --audio-format, --audio-quality | E | Native audio-only selection | F; no transcoder | Quality, not encoding profile | F only | Yes | Lossy encoding expectations | P1 | Missing explicit audio conversion, not missing audio-only downloading. |
| Post-process | --remux-video | A | Compatible containers | F | Container | F | Yes | Copy-only codecs | P1 | Own FFmpeg remux is equivalent functionality. |
| Post-process | --recode-video | F | — | X pass-through, not app feature | Args | — | Yes | CPU/lossy output | P3 | Broad transcoder deferred; no hidden transcode repair. |
| Post-process | --postprocessor-args, --use-postprocessor | I | — | X guard | — | C | Yes | Arbitrary commands/code | WONTFIX | Cannot inject arbitrary processing around an authenticated job. |
| Post-process | --keep-video, --no-keep-video, --post-overwrites, --no-post-overwrites | G | App workspace/conflicts | P + X | Recovery/Args | P,Q only | Yes | Disk/overwrite | P3 | Typed app final conflict policy exists; not all upstream intermediates exposed. |
| Post-process | --embed-subs, --no-embed-subs, --convert-subs | A | Embed/sidecar subtitle choices | S,F | SubtitleOptions | EnhancementTests | Yes | Container/track compatibility | P1 | Subtitle conversion only; video/audio remain stream copy. |
| Post-process | --embed-thumbnail, --no-embed-thumbnail, --convert-thumbnails | E | — | X pass-through | Args | — | Yes | Artwork/size/content | P2 | Cache normalization for notifications is not embedding/export. |
| Post-process | --embed-metadata, --no-embed-metadata, --embed-info-json, --no-embed-info-json | E | JSON sidecar only | P; X pass-through embedding | Sidecar/Args | P only | Yes | Embedded private data | P1 | No typed media tagging/privacy field-selection pipeline. |
| Post-process | --embed-chapters, --no-embed-chapters | B | SponsorBlock Mark/MKV | S,D | SponsorBlock options | EnhancementTests | Yes | Experimental sources | P2 | Mark owns chapter embedding; arbitrary chapter mode not fully typed. |
| Post-process | --parse-metadata, --replace-in-metadata | G | Template only | X | Args | X only | Yes | Metadata transformation | P3 | No expression editor or guarantees against harmful rewriting. |
| Post-process | --xattrs | F | — | X pass-through | Args | — | Yes | Filesystem metadata portability | P3 | No xattr UI; Windows CreationTime is separate. |
| Post-process | --concat-playlist | H | Individual outputs | X may accept, not qualified | Args | — | Yes | Timebase/codec mismatch | WONTFIX | Do not advertise safe playlist/Part concatenation. |
| Post-process | --fixup, --ffmpeg-location | B | Managed FFmpeg path | T,F,D | Tool settings | T,F | Yes | Verified executable | P0 | Fixed tool ownership; arbitrary location blocked in custom args; upstream fixup not typed. |
| Post-process | --exec, --no-exec | I | — | X guard positive option | — | C | No | Arbitrary commands | WONTFIX | Especially unsafe with credentials and raw metadata. |
| Post-process | --split-chapters, --no-split-chapters, --remove-chapters, --no-remove-chapters | E | Details chapters only | M; X pass-through | Args | M only | Yes | Cut accuracy/naming | P2 | No chapter-to-files editor/export model. |
| Post-process | --force-keyframes-at-cuts, --no-force-keyframes-at-cuts | J | No promoted checkbox | Upstream; X may accept | Args | No A/V qualification | Yes | Re-encoding/quality/CPU | P2 | Must not silently “repair” Experimental Remove by adding transcoding. |

## SponsorBlock Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| SponsorBlock | --sponsorblock-mark, --sponsorblock-remove, --no-sponsorblock | B | Off/Mark/Experimental Remove + categories | S,D | Defaults/per job | EnhancementTests | Yes | External segment service | P0 | Mark explicit MKV; Remove has human-confirmed frozen frames/timing defects. |
| SponsorBlock | --sponsorblock-chapter-title, --sponsorblock-api | H | — | X reserved | — | C,S | Yes | Remote endpoint/data | WONTFIX | Canonical app options own categories and endpoint; no arbitrary API override. |

## Extractor Options

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Extractor | --extractor-retries | B | App bounded retries | Q + X | Retry settings/Args | ResilienceTests | Yes | Bounded attempts | P2 | Separate upstream retry layer, not missing app retry system. |
| Extractor | --allow-dynamic-mpd, --ignore-dynamic-mpd, --hls-split-discontinuity, --no-hls-split-discontinuity | J | LIVE/recovery | L + upstream | LIVE parts/Args | L only | Yes | Discontinuity/playability | P2 | Separate verified parts; no proof of every protocol/fragment transition. |
| Extractor | --extractor-args | G | Custom args only | X | Args | X only | Yes | Source-dependent auth/behavior | P2 | Future validated source profiles; no catalog-sized static settings. |

## Preset Aliases

| Category | yt-dlp option/feature | MTD support | UI | Backend | Persistence | Tests | Source dependent? | Security implications | Priority | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| Preset | -t / --preset-alias mp3, aac, mp4, mkv, sleep | H | Own container/audio/pacing UI | X guard; F,Q alternatives | Typed existing models | F,Q,C | Yes | Hidden option expansion | WONTFIX | Presets mix format/transcode/sleep behavior; use explicit future models. Same long option also listed under General. |

## Roadmap, not implementation

P0 first: complete a separately reviewed, consent-bound desktop→browser refresh/ACK design with one bounded retry; validate revoked/stale/offline/closed-browser behavior. Harden remaining expert-argument credential/network/alias edge cases, use a maintained PSL if broad scope validation is to be promised, and qualify real authenticated sessions only with the owner's consent. Native registration/revocation/cold-start and clean-PC browser matrices are release gates, not additions from this audit.

| # | Missing feature | Why / future UI | Models and dependencies | Required tests | Complexity / risk |
|---|---|---|---|---|---|
| 1 | Durable download archive | Avoid repeated completed downloads; Advanced | Archive service/key identity/storage separate from History; success-only commit | Completed vs failed, playlists, concurrent writes, rename/delete, migration | M / private history, false skips |
| 2 | Bandwidth cap | Predictable shared network; Basic | Typed rate cap + validated units, distinction per job/global | Argument units, concurrent budgets, persistence, cancellation | M / upstream throttling semantics |
| 3 | Bounded fragment concurrency | Faster compatible downloads; Advanced | Separate fragment limit from VOD/LIVE slots | HLS/DASH, retry/loss, server rejection, max bounds | M / server load and recovery |
| 4 | Explicit audio encoding | MP3/AAC preferences; Basic + Advanced quality | AudioEncodingOptions, intentional FFmpeg transcode, validated output/quality | Native vs encode, duration/A/V, codec/container, tags, failure | L / lossy conversion/CPU |
| 5 | Codec/FPS/HDR preferences | Predictable device compatibility; Advanced | Extend FormatSelector criteria, fallback explanation, keep exact available IDs | HDR/SDR, fps ceilings, absent codec, container/DRM, playlist per-entry | L / source metadata and no silent recode |
| 6 | Selected metadata/artwork embedding | Portable media library; Advanced | TagOptions + privacy field allowlist, bounded thumbnail, FFmpeg path | Container tags, Unicode, cover formats, no credentials, sidecar unchanged | L / privacy and metadata mapping |
| 7 | Date filters + playlist index expressions | Controlled bulk selection; Advanced | Preview filter model, explicit indices/ranges, persistence | Missing dates, unavailable entries, ordering, max batch, date zones | M / no accidental broad expansion |
| 8 | Chapter export/selection | Save named sections; Advanced | ChapterExportOptions over existing chapter metadata/range model | Bad boundaries, Unicode filenames, collisions, keyframes, subtitle exclusions | L / source boundaries and accuracy |
| 9 | Validated extractor profiles | Manage source-specific settings without 1,750 presets; Expert | Small typed per-source configuration, capability/version ownership | Unknown/retired args, upgrade, secret rejection, scope isolation | L / fast upstream change |
| 10 | Explicit network/proxy profiles | Connectivity where required; Expert | Credential-separated proxy profile, routing/TLS defaults | Credential redaction, no plaintext persistence, failures, IPv4/IPv6 | L / credential handling and TLS |

Do not add username/password/netrc commands, plugin loading, `--exec`, arbitrary downloader/postprocessor execution, insecure TLS controls or “mark watched” to normal UI merely to increase coverage. No score based on “percentage of CLI options exposed” is a useful release metric.

## Coverage and interpretation

The companion report contains mechanically checked grouped-row status counts and the 250-option name coverage result. A/D absence is not inferred from source-only descriptions: first-class UI/backend/persistence/test evidence is required. No D/UI-only behavior was identified in this audit's matrix; this is not a claim that the entire application has no UI bugs. Expert pass-throughs remain unqualified beyond the tokenizer's specific regression cases.
