# Supported sources

**1.0.0 release preparation — 2026-10-06:** active development/CI/release framework pin is `f521f9dfcfe601bf9b6199b88132cccb2380d1bf` (latest fetched `origin/master`). Debug/Release each pass 375 tests and the focused native regression is tracked in the final stabilization closure. The dated platform checks, previous dependency pins and checksums below are historical evidence, not a claim that all extractors are verified. SponsorBlock Remove remains Experimental/default OFF with a known playback defect.

ModernTubeDownloader uses yt-dlp and can work with sources supported by its active extractor set. Selected platforms/features are additionally verified by ModernTubeDownloader. **Extractor availability is not a guarantee that every URL, account state or feature works.**

This document describes the mechanism, not a static copy of all upstream extractors. Qualification below is dated **2026-10-03**, local working tree, not a public release sign-off.

**2026-10-04–05 stabilization update:** the older “no queueable quality” result below predates the conservative opaque direct-media Auto/MKV fallback. The full Archive sample technically passes both Auto OGV and MKV: actual A/V/duration, short decode, JSON and History. Human playback of those full files is not qualified. Input/post-remux probing rejects the OGV custom-range output with invalid timestamps; this is not range-download PASS or platform-wide Verified support. P0 native quality-dropdown variants on the requested verified MFN `bcf2bcb...` pass 100 cycles each: canonical peer and native popup agree. Three raw exact-CLI failures trace legitimate external click/deactivation, not stale peer; the old untraced pass-15 event remains uncertain. Current native ZIP VOD resume reuses actual .part bytes and completes valid media/History. SponsorBlock Remove has user-confirmed edit-point playback defects and remains Experimental/default OFF with warning; Mark / explicit MKV is preferred. The later fetched MFN master is `f521f9df...`; the app intentionally retains its verified pin. Current evidence and revised blocker/post-1.0 triage are in `STABILIZATION_REPORT.md`; historical counts, checksums and platform results below are not current sign-off.

## Runtime catalog

Open **Downloads → View supported services** or **Settings → Download engine → Supported services** (PL: **Obsługiwane serwisy**).

- The active ToolManager executable runs `--ignore-config --encoding utf-8 --list-extractors`, then `--extractor-descriptions`. The command syntax was checked against the installed executable's `--help`, not assumed from memory.
- Names in these CLI outputs are upstream `IE_NAME`. Metadata's class-style `extractor_key` can differ, e.g. `twitch:stream` / `TwitchStream`. Details preserve both identities.
- Exact duplicate names are collapsed. Descriptions match the longest known name prefix because extractor names themselves can contain colons. Missing descriptions remain empty; they do not imply broken support.
- Only the exact upstream ` (CURRENTLY BROKEN)` suffix creates a broken flag. Other unfamiliar names are not guessed broken/experimental.
- The memory cache is keyed by acquired executable path, version, managed/custom identity and SHA-256. Cached opens hash the executable but do not rerun both catalog processes. Relevant ToolManager changes invalidate the cache and refresh an open dialog without app restart.
- Lease version/managed identity is captured atomically with its path. The pair holds a yt-dlp usage lease; if release activates a pending update, the old result is discarded and the new executable is read. A failed new executable cannot display the old catalog as current.
- Missing/provisioning tools show a loading/unavailable state, not a fabricated list. Custom executable catalogs use that executable, not the managed version. Catalog execution has a 45-second bound; ToolManager owns any ongoing provisioning.

The installed managed yt-dlp was **2026.08.19**: **1752 CLI lines / 1751 unique names**, including duplicate `generic`; **136** explicit broken markers. `--extractor-descriptions` returned 1596 lines, including the same duplicate. Upstream omits broken extractors and extractors with suppressed descriptions from that output; different line counts are expected. Counts are runtime observations, never hardcoded assumptions.

Primary upstream references: [CLI options](https://github.com/yt-dlp/yt-dlp#general-options), [audited 2026.08.19 CLI implementation](https://github.com/yt-dlp/yt-dlp/blob/2026.08.19/yt_dlp/__init__.py).

## Search, grouping and performance

Search is case-insensitive over friendly display name, exact technical name and description; exact/prefix matches precede other hits. Default sorting puts scoped Verified entries first, then popular entries, then remaining names alphabetically. Generic is last.

Default mode groups extractor families for presentation, e.g. YouTube and Twitch. Expanding a family shows its actual technical entries, descriptions and per-entry status; the group summary counts verified/broken entries instead of claiming every sibling is verified. Unknown families retain technical names. A name containing `live`, `vod`, `playlist` or `subtitles` does not create a capability.

**Show technical extractors** is off by default and exposes individual runtime names. Popular shortcuts appear only for families present with a non-broken extractor. The small friendly-name map is not a supported-site table; no third-party logo assets are added.

Data filtering/grouping precedes rendering. At most **12 row panels** exist on a page, even for thousands of entries. Expanded family content uses a scrollable text area rather than hundreds of child controls. The native full catalog search/pagination/wheel was exercised; automated regression also covers 10,000 data entries and a 5,000-entry UI with exactly 12 rows.

## Status and Verified criteria

1. **Supported by yt-dlp**: the active executable contains an extractor. This does not claim full MTD QA.
2. **Verified by ModernTubeDownloader — scoped features**: dated real evidence exists for the exact extractor and listed feature/sample. It does not grant all siblings or every URL the same verification.
3. **Limited / experimental**: a documented reason exists. Currently the catalog uses exact upstream broken markers or generic recognition's inherently non-universal scope. URL-specific authentication failures are shown on the check result, not generalized to an entire platform.

`VerifiedSourcesRegistry` is deliberately small. A feature needs recorded public-source metadata PASS; download additionally needs a real bounded download/final-file check; LIVE capture needs a real bounded capture, Stop and save, playable output verification; recovery needs the actual interruption/restart scenario. Subtitle verification needs a real subtitle operation. Historical evidence remains explicitly dated and is not relabelled as a rerun.

Current registry, based on `VALIDATION_REPORT.md` and `RELEASE_CHECKLIST.md`:

| Exact runtime extractor | Scoped real evidence |
| --- | --- |
| `youtube` | Sintel / NASA metadata; three bounded Sintel VOD sections with JSON/timestamp/History; completed NASA replay; tested Sintel manual subtitle sidecar/embedding |
| `twitch:stream` | WildLifeCam metadata; bounded FromNow / Stop and save / ffprobe A/V / short decode; controlled interruption, Parts, Partial and manual/automatic restart |

YouTube active capture and FromStart are **not** marked verified. `youtube:tab`, `twitch:vod`, Vimeo and Archive.org remain **Supported by yt-dlp**, even after a new metadata-only PASS. Sibling extractors do not inherit registry features. Generic is not presented as an ordinary tested platform or a guarantee for every website.

## Check URL and source facts

**Check URL** uses the existing metadata service/parser and yt-dlp/Deno runner. It never invokes either scheduler and never adds queue or LIVE session records. Checks accept validated HTTP(S) URLs, use a two-minute dialog deadline and 20-second socket timeout, and read a flat playlist preview of at most 50 entries. Video watch URLs containing a playlist parameter remain single-video requests.

Results distinguish video, playlist, active LIVE, upcoming, replay and archive processing using actual `_type`, `live_status`, `was_live` and availability. Generic recognition is explicit. Invalid, unsupported, unavailable, private and authentication failures return localized messages, not stack traces. A user may select Netscape `cookies.txt` in Settings; no browser cookie extraction or cookie-path disclosure is introduced here.

**Open in Downloads** reanalyzes media through the existing flow; an authoritatively detected playlist goes through full playlist analysis, not enqueueing the bounded preview. **Open in LIVE** presents the broadcast and still requires explicit start/schedule confirmation. Restricted metadata does not expose an unattended Open action.

The compact analyzed card adds source/type without increasing its configured height. Details gains **Source / Źródło**, showing friendly source, raw runtime extractor/key, media type, availability, live status, safe original URL, ID, actual subtitle/automatic-caption/chapter presence, format count and playlist context. These facts refer to the returned metadata: a flat playlist's zero formats does not prove its individual videos lack formats. Original URL presentation removes credentials, query values and fragments. Live analysis also shows source/extractor/live status.

LIVE ownership remains `LiveSessionService → LiveRecordingScheduler → LiveRecordingExecutor`; VOD remains `DownloadQueueService → QueueProcessor → DownloadJobExecutor`. FromNow does not imply support on every source. Existing FromStart eligibility stays experimental, limited by the existing source policy; arbitrary sources are not promised rewind, reconnect or byte-perfect continuation.

The browser extension remains **YouTube-specific**. Web Remote explicitly states remote adds are YouTube-scoped; its existing HTTPS YouTube-family allowlist is **unchanged**. Broader remote support needs a separate security review.

The pre-existing LIVE → Downloads handler incorrectly reused the YouTube-only external-request validator. This round adds an explicit desktop HTTP(S) analysis entry point for that transition and checked-source navigation. The external protocol entry point remains separately restricted. A native non-YouTube replay-navigation test passes without enqueueing, and a regression checks that desktop HTTP(S) acceptance does not broaden external URL validation.

## Real metadata qualification — 2026-10-03

Native Debug dialog runs used isolated test profiles, no cookies/accounts and the actual managed yt-dlp binary selected as a **custom override** (hence the honest Custom executable label). User AppData, tool versions and settings were not changed. No large media download or recording was started; VOD queue and LIVE session persistence remained empty.

| Public URL | Actual extractor / source | Result | Verification boundary |
| --- | --- | --- | --- |
| [Sintel](https://www.youtube.com/watch?v=eRsGyueVLvQ) | `youtube` / `Youtube`, YouTube | PASS: public VOD, 47 formats, manual/automatic subtitles and chapters | Registry's prior scoped YouTube evidence; this run is metadata only |
| [NASA Roman playlist](https://www.youtube.com/playlist?list=PLF98rxtslw6s) | `youtube:tab` / `YoutubeTab`, YouTube | PASS: playlist, bounded flat metadata | yt-dlp Supported, no full download verification |
| [NASA ISS LIVE](https://www.youtube.com/live/M3HKLzjvKPc) | `youtube` / `Youtube`, YouTube | PASS: `is_live`, 7 formats | Analysis only, no capture or FromStart claim |
| [NASA Artemis replay](https://www.youtube.com/live/46uxUxGpjtY) | `youtube` / `Youtube`, YouTube | PASS: `was_live`, 104 formats | Prior scoped replay evidence; no replay download repeated here |
| [WildLifeCam LIVE](https://www.twitch.tv/wildlifecam) | `twitch:stream` / `TwitchStream`, Twitch | PASS: `is_live`, 6 formats | Prior scoped LIVE evidence; current run metadata only |
| [WildLifeCam ongoing archive](https://www.twitch.tv/videos/2890456079) | `twitch:vod` / `TwitchVod`, Twitch | PASS: upstream still reports `is_live`, 6 formats | Actual live status wins over the URL's VOD appearance; yt-dlp Supported |
| [WildLifeCam completed archive](https://www.twitch.tv/videos/2888679278) | `twitch:vod` / `TwitchVod`, Twitch | PASS: `was_live`, 8 formats, chapters | yt-dlp Supported, not promoted by analysis |
| [Big Buck Bunny on Vimeo](https://vimeo.com/1084537) | yt-dlp diagnostic identifies `vimeo`; no metadata returned | PARTIAL: `The web client only works when logged-in`; localized authentication/cookies guidance confirmed after classifier correction | Catalog Supported; unauthenticated metadata did not succeed; no credentials supplied |
| [Big Buck Bunny on Internet Archive](https://archive.org/details/BigBuckBunny_328) | `archive.org` / `ArchiveOrg` | PASS: metadata VOD, 3 formats; PARTIAL for download readiness | yt-dlp Supported. All three lack `vcodec`/`acodec`; the existing selector offers no queueable quality. Localized limitation confirmed; no guessing or selector change |

Vimeo's exact logged-in phrasing was missing from the existing error classifier. The narrow fix classifies it as Authentication; it does not bypass account requirements or auto-read browser cookies. A regression covers the actual response. One successful analysis never auto-promotes a new source to Verified.

## Historical validation and remaining gates — 2026-10-03

- Restore, Debug and Release builds: PASS, zero warnings/errors.
- Full .NET tests: **349/349** in each configuration, zero skipped (**36 new cases** over the 313-test prior round). This includes parser/description/group/search/Unicode, cache/update/release-race/custom/missing/failure/deadline, metadata classification/null safety/privacy/unknown codecs, separate desktop/external boundaries, semantic IDs, existing ownership/concurrency/process tests and security/localization regressions.
- Browser extension: **16/16**; extension transport/assets were not modified.
- Native source dialog: PL/Light and EN/Dark, full active list, search, technical toggle, pager, native wheel, Settings entry and minimum **760×720** resize PASS. Synthetic VOD/playlist/active/upcoming/replay/unsupported/private/generic results PASS with no queue/LIVE side effects.
- Web Remote security/API tests **7/7** plus explicit PL/EN localization parity PASS; `node --check` on the changed remote JavaScript PASS. The seven are already included in the full .NET count, not seven additional tests.
- Native Details / Source, compact source card, active LIVE/Details/Stop flow with fake tools, playlist selection/persistence/wheel and 20 Settings checkbox/scroll toggles PASS. Explicit Open LIVE and real Archive.org Open Downloads leave both stores empty. Reopening the source dialog executes only one catalog process pair across two opens. These are program-driven native UI checks and own-HWND visual inspection, not a complete human/manual/device matrix.
- A source-neutral LIVE replay → Downloads test PASS covers the separate desktop boundary. Historical real recording/recovery evidence remains in `VALIDATION_REPORT.md`; no new real capture is claimed in this metadata-only round.
- Final ordinary native smoke was repeated in PL/Light and passed. An additional optional quality-dropdown capture repeat stopped at `Collapse` with `ActionUnsupported` after Expand/capture. MFN exposes Collapse only while the popup is open; the capture sequence did not retain that state. This attempt is not counted as PASS, and a repeatable dropdown visual/focus check remains an open QA gate. Source-dialog and Details captures/checks are separate successful runs; no assertion was relaxed to hide the optional failure.
- `Build-Release.ps1` rebuilds the local self-contained win-x64, multi-file, untrimmed 1.0.0 candidate, including this document. The first packaging pass had 353 ZIP entries and no PDB/FakeTool/media tools/cache/logs; final package checksum is recorded outside this ZIP-contained document in `CURRENT_STATUS.md` to avoid a self-referential hash. No commit/push/tag/release/Chrome Web Store publication is made.

**Known limitations / not executed:** authenticated Vimeo or account-only media; real upcoming transition and experimental FromStart; arbitrary-source downloads/capture; full DPI/System-theme matrix; fresh Chrome/Edge/phone-LAN physical testing and human playback. None is implied by parser tests or metadata recognition. UI automation checks use the native MFN semantic bridge; own-HWND captures are visual evidence, not a substitute for all manual/device QA.

### Result classification

- **PASS:** runtime catalog/cache/UI/source facts, scoped registry, metadata cases, safe navigation, build/test/automation and local package preparation.
- **FAIL:** the optional quality-dropdown capture repeat described above did not complete; it is not a successful source-feature check. Required build and unit/integration suites pass. Vimeo's unauthenticated metadata attempt failed because of the upstream account requirement; it is not reported as metadata PASS.
- **PARTIAL:** Vimeo authentication-gated recognition; Archive.org metadata recognized but sample download quality unavailable because codec facts are missing. Arbitrary platform QA is deliberately not signed off.
- **NOT EXECUTED:** account/cookie media, new real downloads/capture, upcoming/FromStart, physical mobile/Chrome/DPI/System full matrix, public publication.
- **KNOWN LIMITATIONS:** generic is non-universal; CLI grouping is not capability discovery; Verified is dated and per feature; flat preview is bounded to 50 and does not describe every entry; unknown codec metadata can block quality selection.

### Changed files in this round

This is a scoped addition on top of an already dirty working tree. Existing unrelated edits remain untouched; the list below is this round, not all `git status` entries.

- New models/services/view: `ModernTubeDownloader/Models/SupportedSource.cs`; `Services/SupportedSourcesService.cs`, `ExtractorIdentityService.cs`, `VerifiedSourcesRegistry.cs`, `SourceCheckService.cs`, `SourcePresentation.cs`; `Views/SupportedSourcesForm.cs`.
- Existing app integration: `ModernTubeDownloader/MainForm.cs`; `Models/VideoMetadata.cs`; `Services/AppServices.cs`, `ToolManager.cs`, `YtDlpMetadataService.cs`, `DownloadFailureClassifier.cs`; `Views/DownloadsView.cs`, `SettingsView.cs`, `LiveView.cs`, `VideoDetailsForm.cs`.
- Resources/remote notice: `ModernTubeDownloader/Resources/Languages/pl.json`, `en.json`; `ModernTubeDownloader/WebRemote/wwwroot/index.html`, `app.js`. Remote host/allowlist and extension files are unchanged in this round.
- Tests/harness: new `ModernTubeDownloader.Tests/SupportedSourcesTests.cs`, `SourceCheckIntegrationTests.cs`; existing `AutomationTestHostTests.cs`; `ModernTubeDownloader.FakeTool/Program.cs`; `scripts/Test-Automation.ps1`, `Build-Release.ps1`. The harness uses own-HWND captures instead of foreground desktop pixels for dependable visual evidence.
- Documentation: `SUPPORTED_SOURCES.md`, `README.md`, `ARCHITECTURE.md`, `CURRENT_STATUS.md`, `CHANGELOG.md`, `RELEASE_CHECKLIST.md`.

ModernFormsNext source remains clean at `6d341963dfa0468c3636ea251e107ee34d0d1272`; no framework or dependency-pin change was made. No test assertion was weakened, skipped or replaced with a timing-only success condition.
