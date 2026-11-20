# Changelog

## 1.3.0

Detailed [English release notes](docs/releases/v1.3.0.md) and a
[Polish summary](docs/releases/v1.3.0.pl.md) describe installation, migration and
limitations. Changes below are relative to 1.2.1. The official release page records
the actual publication time and published assets.

### Added

- Optional Modern Dashboard for Downloads, LIVE, History, Batch Import, Other and
  Settings, with shared native navigation, inspectors, status widgets and themed
  controls. Classic remains available; both shells share one runtime and stores.
- Runtime shell selection and a session-only `--classic-ui` recovery switch.
  New profiles choose in setup; saved existing choices are not silently replaced.
- Collapsible playlist groups over canonical queue jobs, with admitted-job/status
  counts, preserved child identities/order, reveal, retry and confirmed removal of
  completed records without deleting their files. Expansion is session-only.
- Batch Import & Download chains the existing parser, metadata analysis and queue
  admission. Durable phases, multi-file preparation and per-list destinations retain
  independent sessions, accepted paths and stable job IDs across restart/retry.
  Automatic preparation never silently resumes the global paused queue.
- A five-phase Batch progress presenter distinguishes loading, analysis, admission,
  download and finalization, with real counts and unknown/approximate values identified.
- Existing-recording organizer: dry-run, explicit selection, URL/provider/media-ID
  proof, per-list folders, no-overwrite moves, preserved JSON/subtitle/image companions,
  canonical History/queue path updates, durable recovery and guarded undo.
  Verified central metadata may be copied; its original remains in the central store.
- Resumable six-step first-run wizard, one-time existing-profile interface/update-policy
  choice, explicit Skip and manual reopening. Upgrade choice leaves folders, quality,
  authentication and collection data in their existing services.
- Shared searchable language picker with local vector region cues and fourteen
  additional locale catalogs: German, French, Spanish, Italian, Brazilian Portuguese,
  Dutch, Czech, Slovak, Russian, Hungarian, Turkish, Japanese, Korean and Simplified
  Chinese. English/Polish IDs remain compatible; all sixteen catalogs are not claimed
  to have complete native-speaker review.
- Search for specific Settings options with category context, keyboard navigation,
  reveal and highlight, without indexing private input values.
- Six Help topics linked to existing pages, diagnostics, setup and tools; Polish/English
  content and explicit English fallback for other interface languages.
- Version-specific What's New with four categories, reviewed local English/Polish
  fallback and asynchronous official GitHub Release retrieval for the exact installed
  tag. Bounded persistent cache/conditional requests, safe text rendering and source
  labels do not replace the updater or acknowledge notes automatically.
- Separate optional installation at manual foreground startup, off by default;
  bounded preflight, work/recovery deferral and a per-request durable commitment.
- Thumbnail preview/export to PNG/JPEG and recent exports through the existing
  metadata/image pipeline, with no-overwrite output and independent history-save status.

### Improved

- Modern Downloads uses compact real-task rows, an option inspector and observed
  transfer/storage/process widgets. Paged preview retains playlist selection and
  presents actual chosen quality/container information.
- Modern queue presentation retains at most 40 visible rows, including playlist
  headers, and reuses compatible controls. Classic keeps its pre-existing 50-job pages.
  Neither pagination nor grouping caps, renumbers or reorders the canonical queue.
- History builds no hidden per-record card tree at startup. Cancellable filtering runs
  off the UI thread, with retained 40-row pages in both shells. Modern adds date-range,
  sort and media-type filters, an inspector and row actions over the complete data.
- Thumbnail cache is bounded to 32 MiB decoded storage, four concurrent loads and
  512-pixel display images. Caller-owned copies, obsolete-callback disposal and explicit
  window-tree ownership release bitmap bindings, controls, subscriptions and timers.
  Cache limits are not whole-process/native-renderer memory guarantees.
- Modern LIVE adds active/recent projection, source/text filtering, sorting, bounded
  40-session pages and structured session details. The existing independent scheduler,
  recording admission, Stop and save, reconnect and recovery remain authoritative.
- Other exposes working Batch/recent-operation, organizer, thumbnail and diagnostics
  actions. Settings, inspectors, wizard and sidebar use measured viewport-aware layouts
  at large logical text sizes, including 200% sidebar text without horizontal scrolling.
- Batch completion tracking continues with the page closed and after restart. Explicit
  retry reuses failed canonical IDs and accepted destinations. Multi-file selection is
  validated before admission; completed History supplies durable completion evidence.
- New profiles default to automatic stable checks, verified download and installation
  on safe exit. Existing disabled checks, confirmed policy, skipped versions and Later
  deferrals are preserved; ambiguous legacy policy requests an explicit decision.
- Compatible unknown JSON fields round-trip through queue, History, LIVE, Batch and
  nested options. Onboarding does not rewrite collection IDs or cookie stores.
- Browser integration, NativeHost, Web Remote, notifications and managed media tools
  retain their existing services/consent boundaries. Source builds consistently use
  ModernFormsNext `7bf58d06d795d7298c86d9bf9471bbfb46852bab` and .NET 10.

### Fixed

- Passive Batch refresh and actual metadata completion retain unchanged rows and
  workflow/list controls rather than cycling the whole layout and closing ComboBoxes.
  Failed journal writes refresh retained state without a fabricated successful commit
  or duplicate queue admission. Real progress remains enabled.
- Recent download throughput uses observed media-byte deltas and elapsed time. Graph
  and summary share a canonical sampling interval; resumed counters, counter resets,
  terminal phases and missing UI ticks do not invent bytes or uninterrupted history.
  Explicit KiB/s/MiB/s labels distinguish payload rate from OS process/network figures.
- Informational labels/icons retain surface colors after clicking; focused inputs use
  a border. Theme changes update read-only text caches, retain Downloads drawer/drafts
  and restore Settings geometry across shell reattachment.
- Typography refresh tolerates callback-driven child replacement/reparenting. Tool
  records publish on the UI dispatcher. Settings search Enter does not also toggle
  the revealed option. Measured startup-setting height no longer recursively alternates
  through SizeChanged into a native stack overflow.
- Empty queues and not-yet-admitted Batch lists no longer report a zero transfer ETA
  as an estimate for unfinished work.
- Failed queue/History/LIVE loads preserve original bytes and block unsafe writes/work
  admission; damaged identities are not replaced with invented GUIDs. Legacy LIVE
  migration preserves unknown evidence and rejects conflicting source records.
- JSON reads and atomic publication share a gate for each canonical file path,
  preventing open-file replacement races on Windows without blocking unrelated
  stores or profiles. In particular, a slow reader no longer delays another
  store's final LIVE save and admission of the next recording. Canceled waiters
  and unused gates are released without weakening same-file serialization.
- Torn final Batch journal records are preserved before valid-prefix recovery;
  corruption inside a journal fails closed. Clearing completed jobs does not erase
  their Batch identity or cause retry to admit duplicates.
- Organizer diagnostics distinguish scanned files, identified sources and recordings
  outside imported lists. Inspector selection follows the visible result. Unrelated
  or conflicting JSON is not promoted into identity evidence.
- Organizer move/recovery/undo reject active outputs, blocked stores, changed content,
  conflicting IDs and occupied paths, retaining the last verified file copy. History
  only publishes a relocated record after its atomic save succeeds.
- Failed setup, language or update-policy saves do not publish successful decisions.
  Incomplete journey markers do not classify a legacy profile as new. Unreadable
  settings/update state cannot be overwritten with defaults.
- Update initialization is serialized with policy changes and restores staged-package
  notifications. Rollback continues past unchanged/locked destinations and preserves
  incomplete recovery evidence. Shutdown drains remaining owners after checkpoint errors.

### Known issues and qualification boundaries

- **Accepted confirmed issue:** [ModernFormsNext #534](https://github.com/ProGraMajster/ModernFormsNext/issues/534)
  affects maximization against a persistently visible Windows taskbar. Use a restored,
  non-maximized window; taskbar auto-hide may help. No framework workaround is included.
- Some independent popup/tooltip/focus scenarios remain **NOT QUALIFIED**. Physical
  5K/225% DPI is not qualified for this release. The long memory/leak soak was **NOT
  EXECUTED**. Targeted successful runs do not turn those omissions into PASS.
- All sixteen languages have not received complete native-speaker editorial review.
  Bundled Help/notes provide PL/EN; remote official release text is English.
- Source availability and private/authenticated media depend on upstream services and
  valid access/cookies. Existing LIVE FromStart, SponsorBlock Remove, keyframe/range,
  subtitle and browser integration limits remain; no all-source guarantee is made.
- Organizer identity must be proved; filenames and an arbitrary adjacent JSON are not
  enough. Completed LIVE parts are excluded. Corrupt journals require investigation;
  interrupted staging evidence is retained. History/queue updates are separately
  atomic and recovered through the journal, not a cross-filesystem transaction.
- Synthetic fixtures/fault injection are not physical power-loss/disk-removal tests or
  measurements from another user's computer. No full device/accessibility/pixel-perfect
  certification, Authenticode signature, browser-store or NuGet publication is claimed.


## 1.2.1 — 2026-10-18

Stability/performance patch; Classic UI and the existing download/LIVE ownership remain.

### Fixed

- Apply download/merge progress before phase completion instead of allowing delayed callbacks to overwrite interrupted/terminal job state.
- Pause queue admission when the destination folder/drive becomes unavailable.
- Protect queue persistence after disk-full/write failures and require a successful
  writable probe and current checkpoint before explicit Resume.
- Preserve VOD/LIVE partial and recovery media on storage failure.
- Improve Windows notification provider fallback and bounded Shell identity lifetime
  through the pinned ModernFormsNext notification fix; notifications remain optional.
- Accept bounded Batch JSON arrays, single `url` objects and `urls`/`items` envelopes,
  with localized schema errors and examples instead of technical exception text.
- Move heavy Batch import/handoff work off the UI context; avoid rebuilding thousands
  of download cards and quadratic queue-status reconciliation.
- Reconcile missing/interrupted queue jobs and respect filtered selection/action scope.

### Improved

- New Batch sessions default to one analysis worker. Downloads retain the existing
  one-job default and preserve explicitly saved concurrency preferences.
- Render at most 50 queue cards per page with card reuse and page-aware reveal.
- Separate preparation/download processing progress; show advisory size/transfer ETA
  only where data is sufficient, with unknown values explicitly indicated.
- Coalesce progress, journal appends and checkpoints; improve cancellable chunked handoff.
- Add correlated process diagnostics without changing retry/authentication policy.

### Known limitations

- Source HTTP 403/500/availability errors are not claimed fixed. SponsorBlock Remove
  remains Experimental/default OFF; LIVE FromStart remains Experimental/source-dependent.
- MFN #204/#205 typography, physical Windows 11 23H2, exhaustive device/DPI/soak and
  clean-PC updater qualification remain follow-ups. No Modern Dashboard changes included.

### Historical candidate qualification

The following describes the earlier local 1.2.1-candidate.1, not an additional release.

- Pause VOD admission and new media phases after confirmed disk exhaustion; retain partials
  even when ordinary failed-download cleanup is enabled.
- Latch failed queue persistence, retain unsaved runtime state, and require a writable probe
  and current checkpoint before explicit Resume. Known-size preflight includes a reserve.
- Integrate the pinned MFN notification fix (PR #271): cached native-provider failure/fallback and bounded,
  callback-safe Shell identity leases. Log backend/reasons and coalesce repeated warnings.
- Accept bounded streaming JSON arrays, single `url` objects and `urls`/`items` envelopes;
  retain parser limits and show localized errors plus supported-format examples.
- Correlate job attempt/process-role diagnostics. Classify unavailable playlists and exhausted
  incomplete reads without adding retries or changing consent-bound authentication refresh.
- Minimum Windows metadata, LIVE scheduling, updater/security policies and public 1.2.0 stay unchanged.

## 1.2.0 — 2026-10-11

### Added

- Add a centralized native MFN tray controller, work-aware close confirmation, hidden autostart and same-instance manual restore.
- Add explicit current-user Windows Run registration/repair and localized startup/tray settings.
- Add stable GitHub application-update checking, bounded SHA-verified staging and an independent wait/backup/rollback updater.
- Add Other as a working Batch/recent-session hub.
- Add streaming TXT/CSV/JSON batch preparation, bounded metadata workers, durable sessions, filtering/paging and explicit canonical queue handoff.
- Add versioned appearance profiles, four presets, semantic custom colors/contrast preview, per-page options and import/export/reset.
- Add central absolute 80–200% logical text scaling, independent of Windows DPI, with live updates and persistence.
- Organize Settings into eleven localized categories; keep operational settings independent of appearance.

### Changed

- Update the shared MFN development/CI/release pin to clean master `d36015711f63945a49025675acb9aca3df60101d`.
- Use the canonical `ModernTubeDownloader.slnx`, including the independent Updater project.
- Package the self-contained GUI, NativeHost and Updater together, with notices, operational
  guides, a separate symbols archive and SHA-256. Release contains no Debug Automation endpoint.

### Known limitations / post-1.2 follow-up

- Some tab labels and numeric editors do not fully follow global text scaling due to
  ModernFormsNext [#204](https://github.com/ProGraMajster/ModernFormsNext/issues/204) and
  [#205](https://github.com/ProGraMajster/ModernFormsNext/issues/205). Owner-accepted, not release blockers.
- Niektóre etykiety kart i edytory liczb nie podążają w pełni za skalowaniem tekstu.
  Po naprawach #204/#205 planowana jest aktualizacja pinu MFN i poprawa w 1.2.1.
- Exhaustive DPI/large-font/theme/resize, login/tray soak, clean-PC updater, physical
  devices, native file/color dialogs and 20 distinct real Batch sources remain post-release QA.
- SponsorBlock Remove remains Experimental/default OFF with confirmed edit-point defects;
  LIVE FromStart remains Experimental/source-dependent. No new account-session qualification.
- No code signing or Chrome Web Store/Edge Add-ons/NuGet publication.

## 1.1.0 — 2026-10-11

- Browser extension: PL/EN live language switching, real Options/reset, generic HTTP/HTTPS Analyze/QuickQueue handoff and preserved YouTube watch/Shorts/playlist/LIVE modals. Completed `/live/` replays are classified by desktop metadata as VOD; active/upcoming recordings require confirmation. Stale content-script cleanup prevents extension-reload errors without page reload loops.
- Web Remote: Follow app / PL / EN, short single-use pairing codes, bounded attempts, HttpOnly sessions, optional explicit HTTPS and mobile-friendly numeric time-range fields; idle polling preserves forms and unchanged UI nodes.
- Optional native Windows notifications: Downloads/LIVE events, bounded progress, actions, privacy and cached thumbnails, with capability-driven App SDK/WinRT fallback. Cached WebP is normalized locally to bounded PNG.
- Opt-in browser cookies: user-chosen domains/identities and exact runtime permission grants, standalone NativeHost, DPAPI CurrentUser storage, exclusive None/Manual/Synced sources and canonical attempt-owned yt-dlp `--cookies` integration. Manual cookies.txt remains supported.
- Extension cookie profiles can be removed from the visible list after acknowledged deletion. Active profiles revoke first; pending deletion errors remain visible. Browser cookies and native anti-replay tombstones are preserved.
- Auth-failure refresh: consent-bound desktop → Native Messaging → worker request, correlated accepted newer snapshot, new jar and exactly one bounded retry. Unavailable/denied/timeout/second-auth failures require explicit Sync/Retry. LIVE retries occur only after the failed part has been finalized/preserved.
- Expert-argument ownership validation now resolves upstream option spellings, case-sensitive aliases, unique abbreviations, inline values and short clusters; credential/execution/config/plugin/debug bypasses are rejected with canonical reasons, while reviewed safe controls remain available.
- Fix intermittent notification-thumbnail cleanup locks: Skia file handles could be inherited by parallel child processes. Decode bounded managed-read memory instead; regression stress uses 500 cycles without delete retries or sleeps.
- Pin clean ModernFormsNext master `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3` consistently in development/CI/release. Harden sequential Debug/Release tests, zero-skips/warnings gates, standalone NativeHost packaging, notices, separate symbols and ZIP privacy inspection.
- Document the actual yt-dlp feature inventory and deferred 1.2+ roadmap; no mass roadmap implementation.

Known limitations: SponsorBlock Remove remains Experimental/default OFF with confirmed edit-point defects; LIVE FromStart is Experimental/source-dependent. Cookie session validity depends on source/account; moving the installation requires native registration repair. Edge/Brave/Chromium, exhaustive DPI/devices/clean-PC/soak and every Windows provider capability are not fully qualified. No browser-store publication.

### Historical pre-release records

## 1.1.0-candidate.5 — Unreleased, 2026-10-10

- Add opt-in selected-domain/Custom cookies in extension Options, exact runtime permissions, metadata-only status, event/alarm/startup synchronization and ACK-gated handoff.
- Add standalone NativeHost and shared Authentication library: explicit per-user browser/ID registration, bounded versioned protocol, parent/origin validation, DPAPI CurrentUser, replay-safe versions/tombstones and atomic encrypted storage.
- Centralize exclusive None/Manual/Synced yt-dlp authentication and attempt-owned Netscape jars across metadata, playlists, downloads, subtitles, source checks and LIVE probe/record/reconnect; preserve existing schedulers/recovery.
- Reserve credential/alias/command/traffic-dump options and credential headers; scrub known values before diagnostics and metadata output. Add cookie/security/process/extension regressions and package hygiene.
- Detect physical AppData redirection before registering an unusable native manifest. Fix pending multi-profile revocation permission lifetime and retry after optional API regrant; real selected-domain Chrome sync/change/delete qualified with synthetic example.com only.
- Audit all 250 distinct long options in the managed yt-dlp help, with grouped feature status and Top 10 roadmap. No mass feature implementation. Desktop-triggered auth refresh and real-account/other-browser qualification remain incomplete. No publication.

## 1.1.0-candidate.4 — Unreleased, 2026-10-10

- Update the central development/CI/release MFN master pin to `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`, without modifying framework source.
- Normalize existing JPEG/PNG/WebP thumbnail cache entries locally to persistent bounded PNGs for Windows notifications; no extra image downloads. Use capability-driven typed compact placement and text-only fallback, preserving privacy/OFF behavior.
- Log cache format/lifetime/selection and each native degradation warning, without thumbnail URLs; add regression coverage for Windows WebP rejection, normalized content, capability fallback and source-neutral LIVE/replay routing.
- Offer Add to queue alongside Analyze in /live/ modal and popup. Desktop metadata remains authoritative: completed replay enters regular VOD; active/upcoming requires existing LIVE confirmation. Preserve query, quality ceiling and extension invalidation cleanup.
- Native thumbnail visibility and absence under privacy/OFF confirmed by the owner; real Chrome replay QuickQueue created one paused VOD job, with no LIVE session. See candidate.4 report for scope and remaining limits. No publication.

## 1.1.0-candidate.3 — Unreleased, 2026-10-09

- Pin development/CI/release to freshly fetched ModernFormsNext master `16af62c050b346107ee37ca38dc95976ab2de94c`, including official AppSdk/WinRT system notification providers.
- Add an optional central Downloads/LIVE notification consumer, detailed PL/EN preferences, privacy/cached images, event throttling/deduplication and bounded trusted-state actions over existing single-instance IPC.
- Keep language-triggered progress updates capability-driven, limited to already-shown notifications and event-throttled; clear progress identity bookkeeping when a LIVE session is removed.
- Fix stale extension realm after unpacked reload: shared runtime identity guard, local translation snapshot and cleanup of observers/timers/listeners/button/modal; no new permissions or transport.
- Fix Windows App SDK's default Content glob duplicating embedded branding, align publish/application/test TFMs and set the Windows projection version before first restore.
- Read one framework pin in workflows, explicitly separate generic and RID restore graphs, prevent concurrent release packaging, require zero skipped tests and inspect the final ZIP for forbidden artifacts/keys/runtime data.
- Retain v1.0.0 and candidate.2 historical validation. No commit, push, tag or publication.

## Unreleased — 1.1.0 candidate

### 1.1.0-candidate.2 — local, 2026-10-08

- Pin fetched MFN master `9214c8f5cec66e3731e5bf118cc8bfd5a9fba623`; review platform-service, shared picker and Windows additions without modifying framework sources.
- Distinguish available/used/expired/inactive/attempt-limited pairing codes; ensure one code on enable/save, preserve existing sessions on New code and replace token/loopback guidance.
- Default Web Remote to Follow app with safe public language bootstrap and runtime login/dashboard synchronization; retain persistent manual/browser overrides.
- Stop unnecessary idle polling DOM rebuilds and label rewrites; preserve unchanged queue/LIVE nodes and form edits.
- Add actual extension Options, confirmed owned-key reset and immediate popup Auto/PL/EN flag controls shared with live menus/buttons/modals.
- Preserve generic transport, YouTube hidden quality group and HTTPS security.

### Historical candidate.1 changes

- Update development/CI/package ModernFormsNext master pin from `f521f9dfcfe601bf9b6199b88132cccb2380d1bf` to `cc00609880cdb2f7d0351ee7e69d2d3badd91698` (software rendering backend extraction).
- Web Remote PL/EN browser-language Auto and persistent non-secret language override, without changing desktop language or restarting.
- Single-use 8-character pairing codes (10 minutes), bounded attempts and 8-hour opaque HttpOnly/SameSite sessions; logout and restart/auth-change invalidation. Address-only QR never contains a credential.
- Optional Kestrel HTTPS with explicitly generated IP-SAN certificate, per-user DPAPI-protected private storage, restricted ACL, SHA-256 fingerprint and public-only export. HTTP remains default; certificate trust is manual.
- Chrome i18n PL/EN manifest/popup/YouTube modal/context menus. Independent injection/context-menu preferences and Analyze-first context-menu default.
- Source-neutral v2 HTTP/HTTPS URL handoff, bounded Analyze/Open/QuickQueue intent, desktop metadata classification, Best/Auto for generic quick queue, generic playlist selection and LIVE confirmation. Existing v1 YouTube transport remains accepted.
- Extract the existing YouTube DOM anchor strategy into a site adapter; no additional unverified site adapters or all-site content permissions.
- Security/TLS/localization/transport regressions and local candidate packaging. No release, tag or store publication in this round.
- Fix popup styles overriding `hidden` for generic quality labels/hints and the LIVE QuickQueue action; hidden behavior is scoped to the extension popup, not YouTube.

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
