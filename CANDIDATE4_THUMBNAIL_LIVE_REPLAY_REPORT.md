# ModernTubeDownloader 1.1.0-candidate.4 — thumbnail and LIVE/replay report

Date: 2026-10-10. Local, unreleased candidate; no commit, push, tag, GitHub Release, NuGet or extension-store publication.

## MODERNFORMSNEXT

- Previous pin: `16af62c050b346107ee37ca38dc95976ab2de94c`.
- Latest fetched `origin/master` and final pin: `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`.
- `git fetch origin` ran in the clean `.mfn-master-worktree`. The new commits are native view hosting (`c62647b`) and its merge (`69c5ea2`). The dependency is now a clean detached checkout at the final SHA; no framework source was modified.
- Development ProjectReferences use this checkout. Both workflows read `ModernFormsNextCommit` from `Directory.Build.props`; `Build-Release.ps1` verifies exact SHA and clean state. No separate workflow pin needed updating.
- Active architecture, automation, browser integration, notification and dependency documentation was updated. Historical v1.0.0/candidate.2/.3 results retain their original SHAs.
- Central Version/InformationalVersion: `1.1.0-candidate.4`; AssemblyVersion/FileVersion remain `1.1.0.0`. Consumer Windows SDK projection remains explicitly `10.0.28000.87` before restore.

## NOTIFICATION THUMBNAIL ROOT CAUSE

### Confirmed compatibility defect, not a universal failure of .img

The old cache lookup accepted any image that Skia could decode and returned its original hashed `.img` file. The cache really contains WebP as well as JPEG. The current MFN Windows toast validator accepts PNG/JPEG/GIF/BMP headers, but not WebP. A WebP therefore gets omitted with an `InvalidImage` warning while the text toast can still be Accepted. The coordinator previously logged only the warning count, obscuring the concrete degradation reason.

One existing WebP cache entry was inspected read-only: key `02371238567DB923FBF1CC702C7709EF6A5EB8725EF68E571BC292B3345C3A5E`, 59,248 bytes, header `5249464668E700005745425056503820` (RIFF/WEBP). Its URL is not recorded here. A regression passes real encoded WebP to the actual MFN `WindowsToastContent.Create`: `InvalidImage`, no image element. JPEG/PNG pass the same original-content validation.

The real replay used below had JPEG, not WebP. An A/B native submission of its **original JPEG .img with the old Hero mapping displayed an image**, confirmed by the owner. Thus neither the `.img` extension nor Hero is established as a general no-image cause. The original reported unseen notification's exact source image is unavailable; this report does not retroactively assert that it was WebP. The format boundary is a proven application integration defect, and the post-fix native image path is visually qualified.

### Real material diagnostic trace

Source: public replay `MBxOLJe-ZqE`, analyzed with real yt-dlp. The QA profile was isolated and its ordinary queue paused.

| Field | Observed value |
| --- | --- |
| Media thumbnail URL present | YES; URL itself not logged in notification diagnostics |
| Cache key | `E0A38A282BE7283BBDA7740A424556844F02C8487C8B3E47D3A67AD1A5DE117F` |
| Source cache path, relative to repository | `artifacts/candidate4-qa/profile/thumbnails/<key>.img` |
| Source exists / extension | YES / `.img` |
| Actual source header/type | `FFD8FFE000104A464946000101000001` / JPEG, 1280 x 720 |
| Source size | 258,347 bytes |
| Selected persistent path | `artifacts/candidate4-qa/profile/thumbnails/<key>.notification.png` |
| Selected image header/type | PNG, 512 x 288 |
| Selected image size | 287,749 bytes |
| Privacy / show thumbnails for image tests | OFF / ON |
| Selected backend | Windows App SDK |
| Relevant capabilities | Images, InlineImages, HeroImages, AppLogo; progress/live updates also advertised |
| Images count / common role | 1 / Thumbnail |
| Typed Windows placement | AppLogo (no circle crop option) |
| Show status | Accepted for completion, progress and LIVE presentation |
| Show warnings | None in the corrected native submissions; each warning would now be logged with code/message |
| Before / after Show existence | YES / YES for every image submission |

The selected PNG was created at 14:51:23 local time, reused for submissions at 14:52:46, 14:53:43 and 14:54:29, and remains on disk after QA shutdown at 14:57:03 and the later package validation. Disposal does not delete the source or derived cache. No immediately deleted TEMP image is used.

The common MFN Thumbnail role defaults to Windows Hero, not a small picture beside the text. The compact AppLogo placement is an explicit presentation choice, separate from format compatibility. Windows still owns the final layout; no exact aspect-ratio/native text truncation promise is made.

## NOTIFICATION THUMBNAIL FIX

- Reuse `ThumbnailCacheService` and the existing URL-hash cache. Notification lookup performs **no HTTP**, including on a miss; an in-progress cache load is not consumed.
- Validate file existence, a 3 MiB source limit, actual image header, positive dimensions and a 4 Mi-pixel decode limit. Locally decode using existing Skia, proportionally resize to at most 512 pixels on the long edge, encode PNG, check the 3 MiB output limit.
- Atomically retain `<key>.notification.png` beside the original cache entry. Reuse valid existing output; retain both files through service disposal. Only the write-staging `.tmp` file is removed.
- Typed Windows placement is capability-driven: AppLogo, then Inline, then text-only. Providers without Windows-specific capabilities retain the common image role if Images is supported. No RawXml, new image downloader, framework patch or new scheduling system.
- Missing/invalid/oversized/unavailable cache remains a successful text-only notification. Image degradation never changes download/LIVE state to Failed.
- Privacy ON and thumbnails OFF skip image cache lookup entirely; privacy also replaces the material title/message. Existing older OS history is not retroactively erased.
- Diagnostics log URL presence, hashed key/local path, existence, extension, size, decoded type/dimensions, preferences, provider/capabilities, image count/role/placement, before/after existence, submission status and **every** warning code/message. They do not log signed/private thumbnail URLs.

Regression coverage includes cached JPEG/PNG/WebP, actual Windows raw-WebP rejection and normalized-content acceptance, no-network lookup, missing/invalid/oversize, cache lifetime/reuse, pending load, privacy/OFF, absent Images, AppLogo/Inline/unsupported placement and accepted-with-warning degradation.

## NATIVE VISUAL TEST

The isolated real MainForm/AppServices QA host was launched through Explorer with the official provider under a separate `ModernTubeDownloader QA candidate.4` identity. No user queue, production protocol registration, firewall rule or Windows notification policy was changed. The owner closed their running application before the isolated test.

These tests use a real public replay's metadata and persistent image cache, the real coordinator and native Windows provider. Completion/progress/LIVE states are controlled presentation fixtures: they are **not** evidence of downloading the entire replay or making a real LIVE recording.

| Native submission | Evidence beyond Accepted | Result |
| --- | --- | --- |
| Old JPEG .img / Hero, comparison | Owner confirmed film picture visible | PASS; disproves a universal .img/Hero defect |
| Download completed / normalized PNG | Owner confirmed film picture visible | PASS |
| Download progress, 35% / same PNG | Owner confirmed image beside progress; supplied screenshot | PASS |
| LIVE recording saved / same PNG | Owner confirmed image; supplied screenshot | PASS |
| Privacy ON | Owner confirmed no film image and generic material text; supplied screenshot | PASS |
| Show thumbnails OFF | Owner confirmed no film image, title retained; same screenshot | PASS |

Local-only evidence, deliberately ignored and not packaged:

- `artifacts/candidate4-qa/native-progress-human.png`
- `artifacts/candidate4-qa/native-live-human.png`
- `artifacts/candidate4-qa/native-privacy-off-human.png`
- `artifacts/candidate4-qa/profile/prepared.json`, command result files and `logs/ModernTubeDownloader-20261010.log`

The last screenshot shows both no-image completion notifications: one title-bearing OFF variant and one generic privacy variant. Human visual confirmation, rather than native acceptance alone, qualifies image presence/absence. The prior candidate.3 warm Open app confirmation is historical evidence, not a newly executed candidate.4 action matrix.

## LIVE/REPLAY EXTENSION

Previous behavior hid/disabled Add to queue whenever the URL shape was `/live/`. That blocked completed replay VODs despite the desktop already having metadata-first classification.

The modal now offers **Cancel / Analyze in app / Add to queue**, in the existing PL/EN i18n system. The popup also allows both actions. Shared queue payload construction always produces the existing source-neutral v2 QuickQueue intent; the URL shape is only a contextual label.

Analyze remains a separate action. QuickQueue carries the selected quality ceiling/preference and preserves the existing transport, trusted click, language/options and quality persistence mechanisms. The localized LIVE hint explains that desktop analysis decides replay vs active/scheduled LIVE, and recording never starts automatically from this generic intent.

The existing desktop policy remains the authority:

| Metadata result | Existing route |
| --- | --- |
| Ordinary VOD / completed was_live replay | Regular VOD queue, after metadata analysis and format validation |
| is_live active | LIVE page/analyzed workflow; conscious recording confirmation, no ordinary VOD job |
| Upcoming/scheduled | Existing LIVE confirmation/scheduling workflow, no ordinary VOD job |
| Playlist | Existing selection policy; no automatic whole-playlist queue |
| Unsupported/auth-required | Existing readable analysis/error UI |

No production desktop classification/scheduler rewrite was necessary. Existing SourceCheckService, extractor facts, ExternalMediaPolicy, DownloadsView and LIVE presentation were reused. Active LIVE recording, polling, resume/recovery and concurrency were not reimplemented.

## REPLAY ROUTING

Real Chrome sample: `https://www.youtube.com/live/MBxOLJe-ZqE?t=1409s`.

- Current YouTube page showed one injected MTD button. Click opened the centered localized modal, with the actual long title and all three actions; no immediate handoff.
- Real metadata reported `live_status=was_live`, `is_live=false`, `was_live=true`; source facts classified Replay. Prepared metadata had 47 formats.
- Selected 360p in the modal. The payload retained `t=1409s`, requested QuickQueue and did not set a playlist auto-queue flag.
- At 14:53:04 the QA primary received QuickQueue/360 through the existing protocol/IPC path. Real yt-dlp desktop analysis completed successfully. At 14:53:08 the ordinary queue added **one** job, with no duplicate.
- Persisted job: video `MBxOLJe-ZqE`, quality 360, Queued; selected video `605` plus audio `140-drc`, mp4/m4a, merge required. Requested range is Full: playback timestamp `t` is **not** converted into a download range.
- `live-sessions.json` remains `[]`; no LIVE session/scheduler job. Queue stayed paused and no full-media download was started. Desktop normalization may canonicalize the stored webpage URL to `/watch`; that does not change the intent/classification/range result.
- Reopening the fresh modal restored 360p; Cancel closed it without a further handoff. The QA application closed normally; no process remains running.

Local evidence: `artifacts/candidate4-qa/replay-modal.png`, prepared metadata, paused queue persistence and the redacted process/coordinator log. Raw runtime metadata/persistence stays ignored; signed format URLs are not copied into source/report/package.

## ACTIVE LIVE ROUTING

Four metadata regressions use the same `/live/` URL including `t` and `list`: not_live, was_live, is_live and is_upcoming. Metadata, not shape, controls route. VOD/replay produces Full range with no LiveSession; active/upcoming cannot use the VOD FormatSelector. The quality-ceiling case chooses available 720p below a requested 1080p ceiling rather than unavailable/higher 2160p. Existing playlist/auth-required policy tests also pass.

Native automation additionally exercised the existing active-LIVE workflow, Details and persistence with the explicitly synthetic tool fixture (EN/Dark), and LIVE-to-replay navigation (PL/Light). These checks do not imply a real public active stream was recorded. **Real active LIVE extension handoff: NOT EXECUTED**; synthetic coverage is the fallback permitted by the request.

## EXTENSION LIFECYCLE

- Opened the real `/live/` modal, then the owner reloaded the unpacked extension without reloading YouTube.
- The old modal and injected button disappeared; measured owned counts were zero, and no new context-invalidated entry appeared in available page logs. No stale handoff occurred.
- A real stale Add click could not be performed after cleanup because its DOM had already been removed. The retained-handler-after-invalidation click is instead explicitly covered by the deterministic LIVE regression: zero Chrome storage/translation/launch calls and no uncaught error. Do not substitute that for a physically clicked removed button.
- Reloading the page loaded the fresh script: one button, working modal/Add, real desktop replay handoff and remembered quality. No new invalidation error appeared in the available fresh-page logs.
- The already-guarded candidate.3 lifecycle/i18n implementation is unchanged. Older tabs with pre-guard scripts still need a one-time page reload; SPA navigation cannot update their realm.

Current real popup/Options PL/EN interaction and isolated content-script Chrome errors UI are not available as full manual evidence in this round. Popup actions, English modal/focus, locale parity and stale handlers pass deterministic tests; prior user confirmations remain historical.

## VALIDATION

All final product validations below ran against the final source/code changes and the pinned clean framework. Builds/tests were sequential with `-m:1`, shared compilation disabled; no lowered assertions, retries-to-green, skipped tests or warning suppression.

| Check | Result |
| --- | --- |
| Latest MFN fetch / exact clean pin | PASS |
| dotnet restore | PASS |
| Debug build | PASS; 0 compiler warnings, 0 errors |
| Full Debug tests | **442/442**, 0 skipped |
| Release build | PASS; 0 compiler warnings, 0 errors |
| Full Release tests | **442/442**, 0 skipped |
| Extension JavaScript | **30/30**, 0 skipped |
| Web Remote JavaScript | **31/31**, 0 skipped |
| PL/EN resource parity | PASS in complete .NET/JS suites |
| Focused thumbnail/coordinator/external-policy subset | **61/61**; included in full counts, not extra distinct tests |
| Native image visual smoke | PASS; owner confirmation and screenshots, limits above |
| Real Chrome replay → actual desktop analysis/paused queue | PASS |
| Native ordinary range/Details/Settings | PASS, EN/Dark, synthetic media fixture |
| Native LIVE-to-replay navigation | PASS, PL/Light, synthetic fixture |
| Native active LIVE/Details/persistence | PASS, EN/Dark, synthetic fixture |
| Build-Release.ps1 restore/build/tests/publish | PASS; same final .NET/JS counts, 0 warnings/errors/skips |
| ZIP hygiene | PASS; 410 entries; no runtime media tools/test/automation/PDB/cache/log/user-data files |
| Published normal EXE startup / ready / close | PASS, isolated EN/Dark profile, no Automation bridge; custom QA tools, not real provisioning/download |
| Bounded working-tree/reachable-history privacy scan | PASS; 207 candidate files, 6 commits / 265 historical blobs, no findings; exact synthetic security fixtures reviewed |

Final-script TRX: `artifacts/validation/20261010-125949-985eca5b/{Debug,Release}/release-validation.trx`. Native fixture profiles: range `mtd-automation-smoke-3704ba96eaa540d898e2fe391a2d400d`, active LIVE `mtd-automation-smoke-b6b56815b3ef427197880bbde919cdfb`, published startup `mtd-automation-smoke-df7ac690a86d4fdf85e53b3df253a401`, all beneath the system temporary directory.

### Final artifact

- ZIP: `artifacts/release/ModernTubeDownloader-1.1.0-candidate.4-win-x64.zip`.
- Size: **69,378,287 bytes**.
- SHA-256: `114eb259ca65dd1d034bccf189629e64816671855c46685ba2338186365ca8b7`.
- Deployment: win-x64, self-contained .NET, multi-file, untrimmed; no single-file/NativeAOT/ReadyToRun. This does not bundle or certify a full Windows App Runtime installation.
- Separate symbols: `artifacts/release/ModernTubeDownloader-1.1.0-candidate.4-win-x64-symbols.zip`, 21,911,201 bytes; PDBs excluded from ordinary ZIP.
- Application/runtime DLLs/resources, README, notification/source documentation, MIT LICENSE and exact third-party dependency terms/notices included. ToolManager runtime executables are not bundled.
- Prepared CI/release workflows consume the central pin and current assets/tests. No remote CI run or release workflow was triggered for this uncommitted candidate.

### Changes in this round and boundaries

Exact source files changed for candidate.4, relative to repository root:

- `Directory.Build.props`
- `ModernTubeDownloader/Services/ThumbnailCacheService.cs`
- `ModernTubeDownloader/Services/SystemNotificationCoordinator.cs`
- `ModernTubeDownloader.Tests/ThumbnailCacheServiceTests.cs`
- `ModernTubeDownloader.Tests/SystemNotificationCoordinatorTests.cs`
- `ModernTubeDownloader.Tests/ExternalMediaPolicyTests.cs`
- `ModernTubeDownloader.Tests/ReleaseReadinessTests.cs`
- `browser-extension/manifest.json`
- `browser-extension/src/shared.js`
- `browser-extension/src/modal.js`
- `browser-extension/src/popup.js`
- `browser-extension/_locales/pl/messages.json`
- `browser-extension/_locales/en/messages.json`
- `browser-extension/tests/extension.test.cjs`
- `ARCHITECTURE.md`
- `AUTOMATION_TESTING.md`
- `BROWSER_REMOTE_1_1_IMPLEMENTATION.md`
- `THIRD_PARTY_NOTICES.md`
- `SYSTEM_NOTIFICATIONS.md`
- `README.md`
- `browser-extension/README.md`
- `CHANGELOG.md`
- `CURRENT_STATUS.md`
- `RELEASE_CHECKLIST.md`
- `CANDIDATE4_THUMBNAIL_LIVE_REPLAY_REPORT.md`

The substantial pre-existing dirty application/workflow files were preserved; this list is not the whole repository diff. QA harness, shortcut, logs, screenshots and runtime profile are ignored artifacts only. No framework code, transport/security posture, user settings/queue or unrelated feature redesign changed.

Repository remains on `master`, HEAD `5d3f4ac8163739b727203d87f6fd3e5c8546f226`, with pre-existing and candidate changes uncommitted. Index is empty. No commit/push/tag/publication.

## PASS

Both scoped candidate.4 paths: visible native media images and privacy/OFF absence; real Chrome replay QuickQueue into regular paused VOD after actual metadata. Clean latest MFN pin, full Debug/Release, .NET/JS/parity, synthetic active/upcoming safety, native fixture smokes, publish startup and package hygiene.

## FIXED

Skia-decodable WebP crossing a Windows-incompatible image-format boundary; opaque warning-count-only diagnostics; implicit Hero instead of explicit compact capability-driven placement; extension URL-shape suppression of QuickQueue for completed `/live/` replay.

## PARTIAL

The particular original unseen toast cannot be attributed retrospectively to WebP; old JPEG/Hero visibly worked. Native image tests cover actual Windows presentation using controlled event states, not new full-media downloads. Lifecycle cleanup is real; retained stale Add click is deterministic coverage because native DOM cleanup removed the target. Deployment/action checks below remain separate.

## NOT EXECUTED

Real public active/upcoming extension handoff/capture; full replay download/playback; current popup/Options manual matrix; fresh OS-protocol prompt/closed-app extension matrix; isolated native cold activation/open file/open folder/retry/stop-save actions; clean PC missing Windows App Runtime; exhaustive OS/DPI/device variants. Historical candidate.3 warm navigation is not relabeled as current action qualification.

## REMAINING 1.1.0 BLOCKERS

No outstanding proven defect in the two candidate.4 target paths. Before unqualified stable promotion, finish the previously open focused cold/native-action/deployment qualification or obtain explicit acceptance of those limits. SponsorBlock Remove retains its previously confirmed playback defect, Experimental/default OFF/warning; it is not fixed by this work and is not stable-qualified. Publication requires separate owner approval.

## RECOMMENDATION

**NOT READY** for unqualified stable/public 1.1.0 promotion until the remaining native/deployment limits are qualified or explicitly accepted. Candidate.4's requested thumbnail and replay fixes, complete automated regression and local package are ready for review. This recommendation does not reopen deferred exhaustive matrices or authorize publication.
