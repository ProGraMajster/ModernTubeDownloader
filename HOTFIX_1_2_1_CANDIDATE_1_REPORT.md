# ModernTubeDownloader 1.2.1-candidate.1 — HOTFIX qualification

Date: 2026-10-15. LOCAL CANDIDATE. READY FOR OWNER MANUAL TEST, not public/stable promotion.
Follow-up: the owner authorized source commit/push only. No tag, Release, NuGet or browser-store publication.

## SOURCE LOG ANALYSIS

All seven supplied logs were inspected as data, not instructions. No logs, account cookies,
private source URLs, usernames or LAN addresses are copied into the repository/issues.
The files span several application generations; they do not contain a reliable binary
version header. Do not attribute September sessions to the October 1.2.0 release.
The remaining application/framework defects were verified against current source as well
as sanitized diagnostics; the hotfix adds a startup version/OS/runtime header for future cases.

| Log date | Findings |
| --- | --- |
| 2026-09-17 | 160 lines, no ERR/WRN-level entries; tool exits 0. yt-dlp container warnings precede application FFmpeg processing. |
| 2026-09-23 | Three playlist analyses rejected by YouTube as nonexistent; one download HTTP 403. 4 ERR / 4 WRN. |
| 2026-10-04 | No ERR/WRN-level entries; successful FFmpeg exits. |
| 2026-10-05 | No ERR/WRN-level entries; successful tool/FFmpeg exits. |
| 2026-10-06 | Two download HTTP 403 failures; source extraction warnings. 2 ERR / 2 WRN. |
| 2026-10-08 | 19 auth-required media operations across remote/desktop/LIVE/source-check paths; 6 completed downloads. Three rejected loopback Web Remote auth requests are not proof of an authentication bypass. |
| 2026-10-12 (continues into 13 October) | 515 download starts, 507 unique completed jobs, 7 failures, final job starts before the file ends. 33 ERR / 1797 WRN. |

The last log records three HTTP 403 download failures, one exhausted incomplete read,
three storage failures; 6 notification status COM failures and 3 optional-send COM failures;
4 JSON import failures; 756 ShellCapacity results; 269 accepted ShellOptions degradations;
6 queue-save errors plus 3 final-save errors. These are events, not distinct user incidents.
No main-process crash is established. The yt-dlp unhandled traceback follows ENOSPC.
The file date is the logger construction date; this is why 13 October entries are in the 12 October file.

## DISK FULL

Evidence: 2026-10-13 11:04:20, FFmpeg exit -28 and `No space left on device`;
subsequent yt-dlp Errno 28, nine queue-save IOExceptions and an Auth/tmp jar write failure.
Root cause of environmental failure: storage on C: could not accept writes. Logs do not
establish total capacity, free bytes, why it filled, or that an existing queue file was corrupted.
Application defects: admission previously continued after failures; failed persistence was
only logged and no degraded state protected runtime/persisted divergence.

Implemented: central HRESULT/diagnostic classification, shared admission/phase
latch, persistence degradation, explicit writable probe + versioned checkpoint before recovery,
known-size reserve preflight, partial preservation and one localized UI banner.
No real disk is filled in qualification; storage and persistence doubles inject failures.
Confirmed native HRESULTs are language-independent; ordinary sharing/permission IO errors
are not invented as DiskFull. Any queue checkpoint failure still blocks admission as
PersistenceDegraded. Buffered saves stop retrying after degradation; explicit Resume is
the bounded retry boundary. Failure generations/revisions protect recovery from concurrent
phase changes and new failures. A best-effort persisted pause cannot guarantee durability
on an unwritable volume: forced exit may lose unsaved runtime changes.
Known selected-format metadata is reused for size preflight without a second extraction.
LIVE's existing executor also preserves raw fragments on DiskFull with preservation OFF,
skips a fresh recovery remux on the exhausted volume and reports the shared storage latch.
No new LIVE scheduler/retry policy; VOD persistence degradation alone does not stop LIVE.

## NOTIFICATIONS

Evidence: Classic WinRT `ToastNotifier.Setting` throws 0x80070490; later session uses Shell.
Original logs do not record App SDK rejection or the exact native identity/deployment failure.
Do not invent a historical cause or claim the same running provider dynamically switched.

Confirmed framework defects: accepting Classic initialization before probing Setting;
no cached native-provider failure/fallback after initialization; Shell relies exclusively on
native terminal callbacks and retains bounded (32) entries indefinitely when callbacks do not arrive.
Saturation is the MFN transport's own bound, not a Windows limit. Callback loss cause on the
friend's computer cannot be established retrospectively.

Framework implementation: once-per-provider ordered fallback with public HRESULT
diagnostics; Classic initialization probe; dispatcher-owned native cleanup; bounded two-minute
Shell leases reconciled every 15 seconds; reference-safe replacement/late callback handling.
MTD implementation: selected provider/reason/capability logs and bounded warning counters/summary.

## BATCH JSON

Previous: only root arrays. Logs establish root-shape mismatch, not the exact user's JSON contents.
New reader: root arrays of strings/objects, single url object, strict urls/items array envelopes.
Streaming fixed row buffer; retain 256 MB / 250k rows / depth 8 / 16 KiB strings / 64 KiB rows.
No dynamic type activation, polymorphic deserialization, network references or arbitrary envelopes.
Localized schema error and small JSON examples; technical JsonException stays in the log.
Also fixes Render immediately overwriting a displayed import error with Empty/Summary.

## HTTP403 / AUTH

Source rejection is not an OS defect. Keep the existing consent-bound fresh-snapshot auth
retry and configured job retry cap. MaxAttempts=1 remains one job attempt even after yt-dlp
has exhausted its internal retries. No new retry layer or authentication bypass.
Explicit playlist-unavailable and exhausted incomplete-read diagnostics receive safe categories.

## WINDOWS 11 23H2

TFM stays net10.0-windows10.0.26100.0; SupportedOSPlatformVersion stays 10.0.17763.0.
Target SDK is not the minimum OS. Windows 23H2/build 22631 remains inside backend selector tests.
Actual OS edition/servicing policy and optional Windows App Runtime deployment are separate concerns.
Physical Windows 11 23H2 qualification: NOT EXECUTED — no access to that machine.
The official [.NET 10 OS matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
lists Windows 11 23H2 Enterprise but treats 23H2 Home/Pro as out of support (2025-11-11).
The [Windows App SDK matrix](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/support)
lists build 22631 and distinguishes the same edition servicing dates. No minimum/API gate
was raised, but this is not blanket Microsoft support certification of every 23H2 edition.
The friend's edition and native notification runtime/registration still need owner verification.

## MODERNFORMSNEXT

Previous public pin: d36015711f63945a49025675acb9aca3df60101d.
Current fetched origin/master: 18f47b085a59e65eb611e3fd1d38e8dffa3fb19c (PR #268, ItemsControl/virtualized ListView).
The new upstream range changes 26 files but no notification implementation; it does not
duplicate these fixes. Legacy ListView virtualization remains opt-in. App/scroll/accessibility
smoke and full tests are rerun on the new base. The initial d360 qualification is preliminary.
Existing isolated .mfn-master-worktree was clean before editing. The eight local notification
files were retained byte-for-byte when advancing the detached checkout to current master;
no stash/reset/commit or main-framework-checkout edits.
At initial qualification, local ProjectReference used current master plus UNCOMMITTED framework changes, not a fictional new SHA.
Main ModernFormsNext checkout is not modified. #204/#205 are not worked around or reopened here.

### Source-only Git delivery follow-up

The reviewed eight-file framework patch is now commit
`8d8a7679779608b4edbee7dc687c13e3b1a611d9` on `work/notification-resilience-hotfix`,
published separately as [MFN PR #271](https://github.com/ProGraMajster/ModernFormsNext/pull/271).
The app's central pin points to this durable commit; source/CI builds need no uncommitted patch.
This does not claim a protected-master merge. The full Windows backend Release suite was
rerun before the framework push: 410 passed, zero failed/skipped. Original candidate ZIP
provenance and native evidence below remain historical pre-commit qualification of the same patch.
With the clean committed pin, application restore and both solution builds were rerun
(zero warnings/errors), all 749 application tests passed in each configuration and all
92 extension/Web Remote JavaScript tests passed. Evidence is under the ignored
`artifacts/validation/hotfix121/git-upload/` directory. No local ZIP was rebuilt by this follow-up.
No release workflow is dispatched; ordinary branch pushes cannot trigger the tag-only publishers.

## ISSUES

Open + closed search performed in both repositories. Existing MFN #158 is the delivered API,
not a duplicate of these new resilience defects. Targeted disk/Batch/Shell/0x80070490 searches
found no matching bug.

Created after duplicate search and deterministic regression confirmation (no milestone):
- MTD [#1](https://github.com/ProGraMajster/ModernTubeDownloader/issues/1): storage/persistence safety.
- MTD [#2](https://github.com/ProGraMajster/ModernTubeDownloader/issues/2): bounded JSON schema/error UX.
- MFN [#269](https://github.com/ProGraMajster/ModernFormsNext/issues/269): Classic native failure/fallback diagnostics.
- MFN [#270](https://github.com/ProGraMajster/ModernFormsNext/issues/270): Shell stale identity lifecycle/capacity.
Only sanitized technical evidence was published. No raw logs/account data attached.

## VALIDATION

All final source qualification uses MFN master `18f47b085a59e65eb611e3fd1d38e8dffa3fb19c`
plus the eight-file local notification patch. Final application run:
`artifacts/validation/20261015-174040-2c46de80/{Debug,Release}/release-validation.trx`;
packaging log `artifacts/validation/hotfix121/build-release-qualified.log`.

| Gate | Result |
| --- | --- |
| Application restore, Debug solution build | PASS; 0 warnings, 0 errors |
| Full Debug tests | 749/749; 0 failed, 0 skipped |
| Release solution build | PASS; 0 warnings, 0 errors |
| Full Release tests | 749/749; 0 failed, 0 skipped |
| Extension + Web Remote JS | 92/92; 0 failed/skipped |
| MFN solution restore + Debug/Release builds | PASS; 0 warnings/errors, sequential |
| MFN Windows notification backend tests | 63/63 in each configuration |
| MFN portable notification/lifetime contracts | 37/37 in each configuration |
| MFN scroll/FlowLayout regression subset after master update | 34/34 in each configuration |
| Self-contained GUI + NativeHost + Updater publish | PASS; binary version 1.2.1-candidate.1 / 1.2.1.0 |
| NativeHost, cookie refresh, updater transaction/rollback and localization parity | PASS within full application suites |
| ZIP hygiene | PASS; 811 entries, no PDB/tests/automation/tools/runtime data |
| Working-tree/history privacy scan | 275 files, 9 existing commits, 471 path/blob pairs, 0 findings |
| Edited framework-file privacy checks | 8 files, 0 findings; all provenance hashes verified |
| Both Git diff whitespace checks / PowerShell parsing | PASS |

Focused hotfix tests additionally passed 123/123 before the final phase-publication regression
was added to the full suite. Final tests cover native-code classification, storage admission,
degraded persistence/dirty memory, failed/successful recovery checkpoints, concurrent failure
generations, jar write exhaustion, VOD merge and LIVE raw-fragment preservation, and a failure
published synchronously at the Merging transition preventing the FFmpeg process from starting.
No physical disk is filled. The full suites retain the existing bounded consent/auth refresh
and verify MaxAttempts=1 after an internally retried incomplete read.

### Native evidence (host Windows build 26300, not 23H2)

- PL/Dark and EN/Light: startup without Debug endpoint, single-object/envelope JSON,
  malformed JSON localized error that survives timer refresh, supported-format dialog,
  canonical Batch handoff, simulated child-process ENOSPC, pending second job blocked,
  partial files retained, failed explicit Resume checkpoint still paused, unsaved-memory
  warning, explicit work-aware close confirmation and exit. Owned HWND captures inspected.
- Checkpoint fault uses an empty directory at the isolated QA `queue.json` target after
  backing up its file; the target is restored after the test. No ACL change or disk filling.
- Appearance: 100/125/150/175/200/reset and persistence/navigation. Tray: native icon,
  Ask/Hide, same-instance restore, hidden-start argument and graceful exit; no HKCU Run edit.
- Updater: manual Stable check, localized status, no public package install.
- LIVE active fixture/Stop and Save, cookie-source Settings with no real accounts,
  loopback remote protected endpoints/pairing-code availability, range 2–10/Details pass.
- An existing Web Remote test initially waited for exit behind the expected active-work
  confirmation. Fixed the test harness to explicitly confirm exit, not production code;
  rerun passed protected endpoints and clean shutdown. Range smoke then passed separately.
- Release ZIP independently extracted to `artifacts/validation/hotfix121/extracted-a1bd28a9`.
  Production OS UIA (no Debug bridge/coordinates) passed Downloads/LIVE/Other/Batch,
  Updates/Startup/Personalization, 125/reset, candidate version, no-update status and close.
  The harness restored the prior URI association. NativeHost/Updater binary metadata checked.
- A private Debug binary copy without the two optional notification provider assemblies
  selected Shell with sanitized AppSdk/Classic load-failure reasons. One real Shell test
  submission returned Accepted; clean shutdown passed. No OS banner/click claim. Exact
  Classic 0x80070490 remains an injected deterministic regression, not a reproduced OS failure.

Native logs: `artifacts/validation/hotfix121/native-qualified-*.log`,
`published-qualified-en.log`, `native-fallback.log`. Synthetic tools/metadata are not
real provisioning, media playback, account-cookie or clean-PC/physical-device evidence.
Browser extension logic is unchanged; JS regressions passed, no fresh Chrome manual smoke.

Preliminary build/parser/analyzer failures were repaired without suppressing warnings,
skipping tests or loosening input limits. Earlier d360 builds and interim packages are not
the final qualification. Compiler/test gates above are final; expected injected runtime
DiskFull/persistence errors in QA logs are intentional test evidence.

## RELEASE CANDIDATE ARTIFACT

- File: `artifacts/release/ModernTubeDownloader-1.2.1-candidate.1-win-x64.zip`
- Size: 143,752,368 bytes (137.09 MiB).
- SHA-256: `4ee5d0a1512928a66cf8d9bc4d7e43b39cb8ee8b2a835569c7ee4a67c331ae57`.
- Self-contained Windows x64, multi-file, untrimmed; no AOT/ReadyToRun or bundled media tools.
- Separate symbols: `artifacts/release/ModernTubeDownloader-1.2.1-candidate.1-win-x64-symbols.zip`
  (21,982,265 bytes); PDB files do not enter the ordinary ZIP.
- `framework-provenance.json` records base master, eight relative file fingerprints and
  local patch digest `7e1823f4b30a1bb6db8d09b55a866128978b6e3a684c606ec82f04d9a2c29bd0`.
  The GUI assembly records that same patch digest. This is not a new framework commit SHA.
- Default clean framework pin/stable packaging gates remain. Only an explicit
  `-AllowUncommittedFrameworkForCandidate` accepts the reviewed patch scope for a candidate,
  and checks source fingerprints again before packaging. No framework source is in the ZIP.

## CHANGE SCOPE / GIT

Before the source-only delivery follow-up, application branch was `master`, HEAD
`f09fc988e762b083e573bd5d220223d72bcbfd56`, with an empty index, and the framework checkout
was detached at current master plus eight local notification files. The initial qualification
made no commit/staging/push/tag/release/NuGet/browser-store publication; only four sanitized issues.
The subsequent owner authorization permits source commits/pushes and a separate framework PR,
not tags/releases/NuGet/browser-store publication. No runtime data or local artifacts enter Git.
No transport/auth policy, LIVE scheduler, updater/security policy, user queue, real cookies,
minimum OS, unrelated framework controls or MFN #204/#205 workarounds were changed.

Application files (42, including report/tests/docs):

```text
ARCHITECTURE.md
BATCH_IMPORT.md
CHANGELOG.md
CURRENT_STATUS.md
Directory.Build.props
HOTFIX_1_2_1_CANDIDATE_1_REPORT.md
ModernTubeDownloader.Authentication/CookieStore.cs
ModernTubeDownloader.FakeTool/Program.cs
ModernTubeDownloader.Tests/BatchImportTests.cs
ModernTubeDownloader.Tests/DownloadResilienceTests.cs
ModernTubeDownloader.Tests/StorageSafetyTests.cs
ModernTubeDownloader.Tests/SystemNotificationCoordinatorTests.cs
ModernTubeDownloader.Tests/WorkflowIntegrationTests.cs
ModernTubeDownloader/Batch/BatchImportParser.cs
ModernTubeDownloader/Batch/BatchJsonExamples.cs
ModernTubeDownloader/Batch/BatchJsonReader.cs
ModernTubeDownloader/Infrastructure/AsyncProcessRunner.cs
ModernTubeDownloader/Infrastructure/ProcessLogContext.cs
ModernTubeDownloader/Infrastructure/StorageFailureClassifier.cs
ModernTubeDownloader/Models/DownloadModels.cs
ModernTubeDownloader/ModernTubeDownloader.csproj
ModernTubeDownloader/Program.cs
ModernTubeDownloader/Resources/Languages/en.json
ModernTubeDownloader/Resources/Languages/pl.json
ModernTubeDownloader/Services/AppServices.cs
ModernTubeDownloader/Services/DownloadFailureClassifier.cs
ModernTubeDownloader/Services/DownloadJobExecutor.cs
ModernTubeDownloader/Services/DownloadPostProcessor.cs
ModernTubeDownloader/Services/DownloadQueueService.cs
ModernTubeDownloader/Services/LiveRecordingExecutor.cs
ModernTubeDownloader/Services/LiveRecordingScheduler.cs
ModernTubeDownloader/Services/QueuePersistenceService.cs
ModernTubeDownloader/Services/QueueProcessor.cs
ModernTubeDownloader/Services/QueueStorageSafety.cs
ModernTubeDownloader/Services/SystemNotificationCoordinator.cs
ModernTubeDownloader/Views/BatchImportForm.cs
ModernTubeDownloader/Views/DownloadsView.cs
README.md
RELEASE_CHECKLIST.md
scripts/Build-Release.ps1
scripts/Test-Automation.ps1
SYSTEM_NOTIFICATIONS.md
```

Framework files in the isolated worktree only:

```text
ModernFormsNext.SystemNotifications.WinRT/WinRTNotificationBackend.cs
ModernFormsNext.WindowKit.Backend.Windows/Notifications/ShellBalloonNotificationBackend.cs
ModernFormsNext.WindowKit.Backend.Windows/Notifications/ShellNotificationLeases.cs
ModernFormsNext.WindowKit.Backend.Windows/Notifications/WindowsNotificationProviderChain.cs
ModernFormsNext.WindowKit.Backend.Windows/Notifications/WindowsSystemNotificationService.cs
ModernFormsNext.WindowKit.Backend.Windows.Tests/SystemNotificationResilienceTests.cs
docs/system-notifications.md
docs/testing/system-notifications.md
```

## KNOWN LIMITATIONS

Historical native identity/deployment cause and missing Shell callbacks are not recoverable
from these logs. Exact rejected JSON requires a sanitized input sample. No real account cookies,
physical 23H2 tests or media playback are claimed. Native QA uses isolated application data
and the existing startup/notification registration path, not real cookies or HKCU Run edits.
Accepted MFN #204/#205, SponsorBlock Remove Experimental and LIVE FromStart limitations remain.

### Owner Windows 11 23H2 smoke

1. Record edition/build (`winver`), extract this candidate to a writable application folder;
   keep real account cookies/URLs out of screenshots/logs shared publicly.
2. Start, analyze/download one small public VOD; open/close Details, History and LIVE.
3. Import each supported JSON shape and an invalid root; check localized message and examples.
4. Check optional notification backend/reason in logs. Download must still work if notifications
   are unavailable. Banner visibility/click and OS policies require human verification.
5. Do not fill a disk for testing. If a real storage warning occurs, keep the app open, free
   space and explicitly Resume; if checkpoint still fails it must remain paused.
6. Verify partial/recovery files remain; close/restart, verify queue and pause state, check
   PL/EN and the configured text scale. This is owner QA, not an already executed PASS.

P0 FIXED: YES — local candidate stops admission on DiskFull/persistence failure, requires a recovery checkpoint and preserves partial/recovery data; tests and native injected-error flows pass.

FRAMEWORK FIX REQUIRED: YES — implemented/tested and committed separately in MFN PR #271; the app pins that durable revision. Stable promotion still requires framework review/merge and separate release approval; this source-only follow-up does not publish a framework package.

APP FIX REQUIRED: IMPLEMENTED — storage/persistence protection, Batch schema/error UX, optional-notification throttling and process diagnostics.

SOURCE/ENVIRONMENT ISSUE: disk exhaustion, source auth/unavailability/403/network; native deployment cause unconfirmed

READY FOR OWNER MANUAL TEST: YES
