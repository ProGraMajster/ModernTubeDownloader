# ModernTubeDownloader 1.1.0-candidate.3 — notifications and pipeline

Date: 2026-10-09. Local candidate, not published. Application checkout remains `master` at `5d3f4ac8163739b727203d87f6fd3e5c8546f226` with the existing candidate.2 changes preserved. No commit, push, tag, GitHub Release, NuGet or extension-store publication. Historical v1.0.0/candidate.2 evidence is not rewritten as candidate.3 evidence.

## MODERNFORMSNEXT

- Previous active SHA: `9214c8f5cec66e3731e5bf118cc8bfd5a9fba623`.
- Freshly fetched `origin/master`, used by a clean detached worktree: `16af62c050b346107ee37ca38dc95976ab2de94c`.
- Central `ModernFormsNextCommit` in `Directory.Build.props` is consumed by development, CI and release. The solution includes the referenced framework/automation/provider projects for Visual Studio's restore graph. No MFN source patch.
- Official API: `ISystemNotificationService`, `SystemNotifications`, `SystemNotification`, capabilities/access/results/activation/progress contracts and typed `WindowsSystemNotificationOptions`.
- Official `ModernFormsNext.SystemNotifications.AppSdk` and `.WinRT` providers; `WindowsSystemNotificationService.Register` uses Automatic before the UI loop. Shell is the official capability-limited fallback, not a custom toast implementation.
- Shared app/test/publish TFM `net10.0-windows10.0.26100.0`; declared minimum platform 17763. Actual .NET 10 OS support still applies. `WindowsSdkPackageVersion=10.0.28000.87` is explicit before the first restore.

## SYSTEM NOTIFICATIONS ARCHITECTURE

`SystemNotificationCoordinator` subscribes to existing download queue and independent LIVE session events before their processors start. Queue notification snapshots are immutable and captured under the existing queue lock; LIVE snapshots are copied. Existing History is only a trusted source for file actions. No second scheduler, worker, progress timer, native history store or WinRT/COM wrapper.

Native submissions are serialized separately from media execution. Stable state transitions, queue rounds and logical IDs suppress duplicates. A slow initial progress Show is reserved before awaiting the backend; start plus progress does not create two initial Shows. Existing event timestamps bound Updates to about one per second. OFF means no Show, Update, status query, notification image lookup or deferred replay.

Final audit added three deterministic red→green regressions: a language change must not start download progress on a Basic-only provider; LIVE language updates require an already-shown progress notification and share the same throttle; removed LIVE sessions must release progress identity bookkeeping. All three failed before the narrow fix, and all 25 coordinator/policy cases passed afterward. No native media scheduler was changed.

Shutdown unsubscribes and drains notifications, then awaits official provider disposal while the MFN dispatcher is alive, before shutting down app workers. Optional exceptions are logged in full without changing download state. Capabilities gate actions/images/progress/replacement/updates/urgency/indeterminate/suppress popup/removal; missing optional features degrade rather than become synthetic successes.

## SETTINGS

Settings has a localized System notifications card, semantic control IDs, status, Advanced diagnostics, test button and notification-only reset. Persistence migration fills defaults and normalizes unknown enums. The complete event matrix is in [SYSTEM_NOTIFICATIONS.md](SYSTEM_NOTIFICATIONS.md).

| Preferences | Defaults |
|---|---|
| Enabled / privacy / cached thumbnails | ON / OFF / ON |
| Sound / presentation / dismiss on cancel | System default / Normal / OFF |
| Download started / completed / failed | OFF / ON / ON |
| Download retry / cancelled / queue completed / progress | OFF / OFF / ON / OFF |
| LIVE waiting / transmission started / recording started | ON / ON / ON |
| LIVE connection lost / restored / part saved | ON / ON / OFF |
| LIVE completed / partial / failed / cancelled | ON / ON / ON / OFF |
| Critical application error | ON |

Center only is offered only when actual SuppressPopup is supported. Technical backend details are Advanced-only. New PL/EN keys are covered by the existing catalog parity tests. Critical errors use an existing error boundary; tool-version notification polling was not invented. One shared sound preference avoids excessive per-event controls.

## DOWNLOAD NOTIFICATIONS

- Start: first active transition, not video/audio/merge substage spam; default OFF.
- Progress: `downloads` / `download-<QueueItemId:N>`, initial Show then bounded Update, current percentage/speed/ETA. Requires the actual rich progress/update/replacement capabilities.
- Completion/failure: terminal event, same logical identity replaces progress where supported. Completion offers file/folder; failure offers retry/open app subject to current state and capabilities.
- Retry: one notification for a newly scheduled attempt, default OFF; no countdown banner loop.
- Queue completion: exactly once per round only after all active/pending/waiting/retrying jobs leave the round; completed/failed counts include concurrent jobs.

## LIVE NOTIFICATIONS

Waiting is a state transition, not each 30–60 s poll. Waiting → Pending announces availability; initial Recording announces recording. Reconnecting/next Recording announces connection lost/restored even when Starting is intermediate. Optional new-part events are OFF. Completed, Partial and Failed remain distinct terminal transitions.

LIVE has no fake percentage. Duration/bytes are indeterminate progress only when actual capability supports it; otherwise ordinary text. Updates remain event-driven and bounded. Existing capture/recovery/resume/persistence and independent LIVE concurrency are unchanged.

## ACTIONS

Payloads contain only bounded, versioned group/GUID/view/allowlisted action IDs. Native logical notification/group/action identities must agree. No path, URL or shell command is accepted from notification arguments.

`NotificationActionPolicy` rechecks current trusted queue/history/LIVE state: file/folder needs an existing trusted final file, retry only Failed, stop/save only Recording. Missing/deleted items or files become navigation-only. Downloads tries to reveal its queue card; History/LIVE use their existing views. Exact LIVE-card reveal is not newly implemented.

Existing `ExternalLinkInstance` per-user IPC transports both external links and validated notification intents. A secondary native activation receiver owns only a closeable MFN dispatcher/official provider relay, not AppServices, tools or schedulers. Actual native startup markers are recognized; arbitrary arguments are not. Cold startup consumes the official provider's retained activation after UI/IPC binding.

Warm native Open app is owner-confirmed below. File/folder/retry/stop-save and cold actions have deterministic policy/IPC tests, not a complete real Windows action matrix.

## PRIVACY

Future submissions/updates check current privacy settings again: generic text, no media title/channel/URL/playlist title/image. Image lookup uses only an existing valid URL-hash cache file, bounded to 3 MiB; missing/oversized/invalid headers degrade to text, with no second HTTP request.

Turning privacy ON does not erase notifications already retained by Windows. Lock-screen exposure is also governed by Windows policy; MTD does not silently alter that policy or clear the notification center. App identity remains ModernTubeDownloader.

## WINDOWS BACKEND

Actual local Settings native smoke selected **Windows App SDK / Ready** and returned **Accepted**. PL/Dark and EN/Light test submission and Settings control/tree capture ran against the real app/provider, not the unit-test fake service.

Direct tool-host launches repeatedly returned Accepted and Windows Push Notification delivery events while the owner reported no banner/center entry. In the final comparison, the same Debug EXE/harness launched via an Explorer shortcut produced a visible notification. The owner confirmed **“Tak, teraz widzę”**, then confirmed that clicking opened Downloads in the same QA app, without a second independent instance; process inspection found one MTD process. Profile identifier: `5f297670a6544024b282007bdc9df68b`, native application PID `18836`, test submission at 19:26:27 local time.

This is a **scoped native PASS** for visible Settings test notification and warm Open app. It is not inferred from Accepted. The precise tool-host versus Explorer-context cause is not established; no Windows notification settings, permissions, services or policy were changed and no registry was manually edited. Normal official provider registration owns app identity changes. MFN documents the same historical distinction in `docs/testing/system-notifications.md`.

A later repeated user click caused Windows to create PID `56432` with `----AppNotificationActivated: -Embedding`; the owner confirmed clicking again and closed that app. This is evidence of a real OS COM launch, not an isolated cold-action/routing sign-off: the subsequent process used the normal profile, and its exact target view/action was not independently verified. It was not killed or silently reused as a QA fixture.

Distribution is self-contained **.NET**, not self-contained Windows App Runtime. Optional App SDK projections/bootstrap files are packaged; the complete App Runtime/installer is not bundled or automatically installed. Eager bootstrap/deployment initializers are disabled. Automatic selection handles known optional-provider load failures; final unexpected registration failures disable only notifications. Published startup/normal UI/no automation bridge/tool validation/clean close passed, then passed again in a fresh isolated copy omitting only `ModernFormsNext.SystemNotifications.AppSdk.dll`. The original publish and Windows installation were untouched. A physically clean PC without App Runtime remains unqualified; missing-provider-assembly startup is not a substitute for that machine or native fallback delivery.

Microsoft Windows App SDK redistribution terms are included from the exact restored packages; they are not described as MIT. Windows projection metadata and CsWinRT MIT notice accompany existing dependency notices.

## EXTENSION ERROR

Real repro: YouTube `/live/MBxOLJe-ZqE`, open MTD modal, reload unpacked extension without reloading YouTube, activate stale modal. Owner reported the new Chrome stack at **`src/i18n.js:12`**. The old content realm kept DOM/event/observer callbacks after Chrome invalidated its API context.

Fix: one shared context guard owns lifecycle, uses runtime identity without issuing a Chrome API probe, recognizes invalidation errors only, and rethrows unrelated failures. Local packaged PL/EN catalogs avoid stale `chrome.i18n.getMessage`. Invalidation disconnects own observers, cancels timers, removes DOM/Chrome listeners where possible, removes button/modal, and makes retained actions sterile. Async startup/storage/menu operations recheck current realm/revision after awaits. Popup/Options/background use the same guard. No new scripting/all-URL permission or new transport.

Post-fix Chrome:

- Reload extension with LIVE modal open → actual `#mtd-download-button` count 0, no `#mtd-modal-backdrop`; stale callbacks no longer launch.
- Owner confirmed **no new error in chrome://extensions**. Old pre-fix error was not counted as a new failure.
- Reload page → one fresh button and usable modal.
- Real LIVE SPA navigation to `/live/cE_Gfws8fyA?t=1473s` → one correctly reattached button, no stale modal/duplicates.
- Ordinary watch `eRsGyueVLvQ`, Shorts `wZujS6R5GBw`, NASA playlist `PLF98rxtslw6s` → one button each; contextual modal/cancel work, playlist shows its title and 3 entries.

Current real popup/Options/background reload matrix and fresh OS protocol prompt/closed-app extension handoff were not executed here. Earlier candidate.2 owner-confirmed popup/Options/Vimeo behavior is historical; deterministic regressions preserve those paths.

### Follow-up: ordinary watch button click — 2026-10-09

The owner's subsequent screenshot is a **real new failure**, not merely an old error-console entry. Clicking the injected button on the already-open watch/Mix tab reproduced it: no modal, with stack `i18n.js:12:36 → modal.js:85:106 → content.js:74:26`. Those positions differ from the current sources (`t` starts on line 12 without an API call there; modal translation is on line 88; the button opens it on line 71). Together with the following same-tab comparison, this identifies the old pre-guard content-script instance retained by that tab. Existing older scripts cannot acquire the new lifecycle owner retrospectively, and SPA video changes do not reload their realm.

Reloading that same tab once loaded the current extension: the button opened the localized modal, then three further open/cancel cycles passed. No new `Extension context invalidated` log appeared after reload; the earlier entries remained visible with their original timestamps. A separate fresh QA tab on the exact video from the screenshot also opened the modal visually and had no invalidation log. No material was handed off or queued in these checks. A local screenshot from the QA tab is in ignored `artifacts/qa-extension-click-20261009/watch-modal.jpg`; no personal browsing URL/title is added to this report.

No further production/transport/framework change was needed. A new regression covers the first stale-button click (before any watchdog tick or modal/storage action), for plain watch and watch-with-Mix URLs: remove stale UI, disconnect observers, avoid Chrome translation/storage and handoff, and keep retained button callbacks inert. The standalone extension suite now passes **30/30**. The 29/29 pipeline count and ZIP below describe the preceding package run; this follow-up changes only the regression test and documentation, not packaged runtime code.

## PIPELINE

Root causes/fixes:

1. First-restore Windows projection property could otherwise arrive too late from transitive props. Central explicit property was validated with a fresh empty local NuGet package directory, one restore, no NETSDK1112.
2. New App SDK default Content globs also treated the existing embedded AppIcon.png as output Content, causing **MSB3030** in test-project copy. Code-first app disables default Content items while retaining the canonical embedded icon, not a duplicate asset or warning suppression.
3. Publish profile had the obsolete app TFM. App/test/publish now share the central actual Windows-projection TFM; automation CLI retains its own legitimate target.
4. Parallel/shared framework outputs and implicit repeated restores risk MicroCom/obj collisions. CI/release build sequentially (`-m:1`, shared compilation OFF); tests target only the application test project, without rebuilding/restoring. One generic restore precedes both configurations; one distinct self-contained RID restore precedes no-restore publish.
5. Upstream automatic package-on-build performed unrelated NuGet packing. Validation/publish passes `GeneratePackageOnBuild=false`; no MFN source edit.
6. A second isolated QA app while another primary was running could exit before exposing a window. Harness now rejects an existing instance with a clear message, without killing it or treating it as an app regression.
7. Release output cleaning is confined to validated generated directories; a named mutex prevents concurrent cleanup/publish and rejects a second invocation. Existing processes using build files cause an explicit stop, not forced termination. Candidate.1/.2 archives are not deleted.
8. CI/release use the central exact clean MFN SHA. TRX/hang artifacts are retained; zero skipped tests are enforced explicitly. Release gates license/public repository/stable version/exact tag-to-HEAD; no push publishes a release.
9. Multi-file untrimmed self-contained win-x64 is retained. Exact ZIP paths prevent accidental selection of older candidates; symbols separate. Exact dependency notices/projection terms are included and unsafe/private/runtime/test/tool files are rejected.

No retries, lowered thresholds, warning suppressions, test skips or deadline extensions were used to make qualification pass. Workflows are prepared, not remotely executed for this uncommitted candidate.

## REGRESSION

- Web Remote: existing Follow app/manual overrides, authenticated pairing/logout/code lifecycle, unchanged polling DOM/focus and numeric range editor are covered by the complete suite. No new authentication/security posture change.
- Extension: generic HTTP/HTTPS/Vimeo hidden controls, options/language, watch/Shorts/playlist/LIVE/context cleanup tests. Transport behavior is preserved.
- VOD/LIVE/playlists: complete application regression suite; fresh native ordinary EN/Dark analysis/quality/range/queue/Settings/Details tabs/clean close, PL/Light active-LIVE completion/Details/persistence and EN/Dark 12-entry playlist preview passed. These use the explicit synthetic tool fixture; existing independent LIVE scheduler/recovery/resume semantics are not reimplemented. Real media acquisition/playback from earlier rounds is not relabeled as a new candidate.3 download test.
- Settings scroll: fresh PL/Light 20 checkbox toggles while scrolled, native wheel/rapid wheel/scrollbar track/thumb drag all preserve alignment of the 14 sections and return to the top correctly. Own-HWND captures were inspected; no manual wheel/layout workaround.
- SponsorBlock Remove remains Experimental/default OFF with the previously confirmed playback defect. No claim that notifications or pipeline fixes repair it.

## VALIDATION

| Check | Current evidence |
|---|---|
| Fresh empty-package-store restore | PASS; no projection bootstrap mismatch |
| Debug build / full tests | PASS, 427/427, 0 skipped, 0 compiler warnings/errors |
| Release build / full tests | PASS, 427/427, 0 skipped, 0 compiler warnings/errors |
| Extension JavaScript | PASS, 29/29 |
| Web Remote JavaScript | PASS, 31/31 |
| PL/EN resource parity | PASS in full .NET suite |
| Notification coordinator/policy regressions | 25 new .NET cases; includes three final audit red→green regressions; fake OS service explicitly distinguished |
| Real Settings native smoke | PASS for control creation/test submission, PL/Dark and EN/Light |
| Visible notification / warm Open app | PASS, owner-confirmed after Explorer launch |
| Real Chrome reload / SPA / contexts | PASS within the exact scoped scenarios above |
| Build-Release restore/build/tests/publish | PASS; same test counts and zero skips/errors/warnings |
| Package hygiene | PASS, 410 entries; tools/test/automation/PDB/runtime data excluded |
| Published EXE startup / simulated optional-provider absence | PASS for normal UI, no automation bridge, four custom QA tools validated, clean close; no real provisioning or clean-PC claim |
| Ordinary native Details / range / Settings | PASS, current EN/Dark harness; synthetic media fixtures |
| Settings scroll | PASS, current PL/Light, 20 toggles + wheel/track/thumb, 14 aligned sections |
| Native active LIVE / playlist preview | PASS, synthetic PL/Light LIVE/Details and EN/Dark 12-entry playlist |

Final local artifact: `artifacts/release/ModernTubeDownloader-1.1.0-candidate.3-win-x64.zip`, **69,365,186 bytes**, self-contained .NET win-x64, multi-file, untrimmed. Separate symbols ZIP. SHA-256: `93227a78afae818b5fd4cd93926626da3bcdf74f15d67dc135cac851b8a6a43d`. Runtime configuration carries .NET/ASP.NET Core 10.0.12; not Windows Desktop or an installed Windows App Runtime bundle.

Final full-script TRX: `artifacts/validation/20261009-174554-9d4d76d5/{Debug,Release}/release-validation.trx`. Native fixture profiles: ordinary `1fdf10f8812f46539ad99f0b925066d2`, Settings scroll `532d69992846470a9bb3fe0397d6e506`, active LIVE `56536d4571c54465bc0ebcd1a4463838`, playlist `96641e0bbafc4ce8b103fc4df99f1ba8`. After the final source rebuild, ordinary native range/Details/Settings/clean-close smoke also passed again with profile `13496796c7154ed7b013efaa77c23bfc`; final published startup/missing-provider checks passed with profiles `0ce9a92424e1453f9563872a7724a42e` / `191546bb70e24fbbad791ec48213f078`. QA screenshots/logs/shortcut stay ignored/local, outside source/package. The index is empty; unrelated prior local changes are preserved.

## PASS

Exact MFN pin/clean framework; zero-warning Debug/Release and all automated tests; empty-store restore; current real Chrome invalidation/SPA/context smoke; actual Settings provider submission; owner-visible notification and warm Open app; native ordinary/Details/range/Settings scroll/LIVE/playlist fixture smokes; self-contained publish/start/clean close with and without the optional provider assembly; package hygiene; scope/publication boundaries.

## FIXED

Stale extension realm callbacks/i18n; async/menu revision lifecycle; first-restore configuration; accidental app-icon Content copy; stale publish TFM; restore/parallel output graph; unnecessary auto-pack; isolated QA primary conflict diagnostics; concurrent release cleanup and exact package/notice gates.

## PARTIAL

Native Windows notification matrix: visible test + warm navigation qualified, not every download/LIVE image/progress/action. Tool-host launch remains unsuitable for certifying visibility; Explorer evidence is separate. Deployment fallback policy/exception isolation have automated evidence, not clean-machine certification.

## NOT EXECUTED

Isolated cold-action/routing verification after process exit (a real OS launch was observed but not qualified); real notification open file/folder/retry/stop-save; native download/LIVE completion/progress/thumbnail banner matrix; physically disabling Windows notifications; clean PC missing App Runtime; current popup/Options/OS-protocol/cold extension handoff matrix; exhaustive OS/DPI/device/permission variants. No mock result is substituted for these.

## KNOWN LIMITATIONS

Provider capabilities and OS presentation policy differ. Privacy does not erase prior OS history. Shell/classic fallback cannot promise App SDK's rich/cold semantics. LIVE routing opens its view but does not add a new per-card reveal API. Complete Windows App Runtime is not bundled. Physical/banner qualification requires a normal desktop launch context.

## REMAINING 1.1.0 BLOCKERS

Obtain focused isolated cold-activation/file-folder/LIVE-action qualification or explicit acceptance of those documented limits before promoting to stable 1.1.0. Final publish/ordinary-native startup/scroll checks have passed. No new proven production defect is claimed from a missing check. Publication still requires explicit owner approval; this round does not authorize Git or publication writes.

## RECOMMENDATION

**NOT READY** for unqualified stable/public 1.1.0 promotion yet. Candidate.3 is implemented and its automated checks/package pass; remaining focused native/deployment checks must be completed or explicitly accepted. Visible test and warm activation are now verified, not blockers due to unobserved banners.
