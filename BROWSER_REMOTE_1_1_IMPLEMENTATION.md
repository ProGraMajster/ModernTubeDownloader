# Browser / Web Remote 1.1 implementation

## Candidate.3 — 2026-10-09

Current pin: `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`. Current notification thumbnail, LIVE/replay routing and pipeline qualification: [CANDIDATE4_THUMBNAIL_LIVE_REPLAY_REPORT.md](CANDIDATE4_THUMBNAIL_LIVE_REPLAY_REPORT.md). Candidate.3 lifecycle evidence remains in [CANDIDATE3_NOTIFICATIONS_PIPELINE_REPORT.md](CANDIDATE3_NOTIFICATIONS_PIPELINE_REPORT.md). Candidate.2 evidence below is historical, not relabelled as current validation.

Real Chrome `/live/` + open modal + unpacked reload reproduced `src/i18n.js:12`. The new shared context guard removes stale UI, observers, timers and listeners; translations use the already loaded local catalog rather than calling invalidated Chrome APIs. Post-fix reload removed the actual `mtd-download-button` and `mtd-modal-backdrop`; the owner confirmed no new extension error. A normal page reload restores fresh injection. No `scripting`/all-URLs permission or transport change.

## Candidate.2 — 2026-10-08, local and unreleased

No commit/push/tag/release/store/NuGet publication. Prior candidate.1 evidence below and public v1.0.0 history are preserved.

### Pairing root cause and security

The reported combination was reproduced with a successful single-use exchange: the code becomes Used while the opaque HttpOnly cookie keeps the browser authenticated. Candidate.1 collapsed Used/Expired/Inactive into one ambiguous “unavailable or used” Settings message. This is a confirmed presentation defect, not evidence of an anonymous auth bypass. The owner does not recall whether the original browser was paired, so its historical cause cannot be conclusively assigned. Unsaved checkboxes affect presentation until Save, not the running listener.

Clean native profile: Remote OFF → pairing ON → enable → Save gives auth-mode true, a fresh code and **401 for anonymous status/queue/live**. Chrome showed the pairing form. Code → dashboard, New code → existing session retained, refresh → dashboard, logout → login are exercised. HTTP/HTTPS integration retains single use, wrong/expired/rate-limit rejection, logout revocation, restart invalidation and auth OFF→ON rejection of stale cookies. Cookie/Origin/Host/CSRF/private bind/CSP/body limits remain unchanged.

Settings now separates Available (mm:ss), Used (existing device keeps its session), Expired, Blocked and Inactive. Start/save ensures one unused code; snapshots/countdown/redraw never regenerate. Used code remains explicit until New code or a save/reconcile action. Status uses pairing-code terminology and loopback/LAN scope, with no firewall changes.

### Languages and extension settings

Remote defaults to Follow app. Public auth-mode exposes only auth-required and normalized desktopLanguage. One two-second visible-tab polling cadence synchronizes both login and dashboard; manual PL/EN or Browser persists locally. Legacy Auto retains browser semantics. Chrome runtime desktop PL→EN→PL, unpaired sync, override and reload were checked; errors/footer also have JS regression coverage.

Extension uses one extensionLanguage preference: browser Auto/PL/EN. Manual overrides load the existing Chrome catalogs. Popup flag buttons have labels/aria, an immediate language menu and real openOptionsPage links. MV3 Options exposes existing injection/context-menu/default-action/last-quality preferences, browser Light/Dark and narrow layout, About/version and a confirmation dialog resetting only owned keys. Storage changes relocalize open popup/options/button/modal and rebuild menus. No desktop sync is claimed: custom protocol has no return channel. No new permissions, Native Messaging or listener.

Owner confirmed the real Vimeo popup PL→EN, Options link and reset flow after reloading the extension. Generic quality-label/hint/select remain hidden together; transport and desktop SourceCheck remain authoritative.

### Additional recording regression

Reviewed supplied 20261008-1545-26.3496994.mp4 (9.7 s), including the Chrome translation bar. Polling unnecessarily rewrote static labels and replaced queue/LIVE DOM every two seconds; the localization loop also targeted the queue container as if it were a label. Polling remains, but only changed language/data invalidates the view. Idle polls retain list nodes, form edits and focus. Session revisions discard responses racing logout. This is UI rebuilding, not an explicit page-reload instruction.

### ModernFormsNext

Previous candidate pin: cc00609880cdb2f7d0351ee7e69d2d3badd91698. Fetched master/clean detached checkout: **9214c8f5cec66e3731e5bf118cc8bfd5a9fba623**. Two new commits: 362d61f (platform services) and 9214c8f (PR #195 merge). Reviewed changes include Android services, shared launcher/share/dialog contracts, cancellation in Windows pickers and additive SystemMessageBox/Windows services. No app compatibility patch or MFN modification was needed. All active development/CI/release/documentation pins updated; v1.0.0 records untouched.

### Final validation

`scripts/Build-Release.ps1` completed against the final application changes, including the recording regression: restore, Debug build/tests, Release build/tests, JavaScript tests and publish all PASS. **402/402 .NET tests per configuration**, **57/57 JavaScript tests (26 extension + 31 Web Remote)**, PL/EN parity, zero compiler warnings/errors and zero skipped tests. Evidence: `artifacts/candidate2-build-release.log` and `artifacts/validation/20261008-155119-f15a3447/{Debug,Release}/release-validation.trx`.

Native Automation on the current MFN: EN/Dark ordinary startup, analysis/quality popup, Settings and Details PASS; PL/Light Settings scroll regression PASS for **20 pairing-checkbox toggles**, exact 100 px content-height delta, unchanged offset and aligned cards, wheel/track/thumb checks. The delta changed from 80 to 100 because the explicit multiline code states need more room; exact assertions were updated, not relaxed. Evidence: `artifacts/candidate2-native-dark.log`, `artifacts/candidate2-settings-scroll.log`.

Fresh native Settings enable/save and real Chrome pairing/login/logout/New code, runtime desktop PL/EN and manual override/reload PASS. This used an isolated app profile and a new server session, not a claimed Chrome incognito context. After the polling fix, a second fresh loopback profile was paired in Chrome; the dashboard and edited URL/focus remained stable across subsequent polling cycles. No material was submitted/downloaded. Screenshot: `artifacts/validation/candidate2-video/remote-fixed.jpg`; supplied-recording analysis: `contact.png` and `polling.png` in the same ignored directory.

Real Chrome YouTube watch, identified Short and NASA playlist: one injected button and context-appropriate PL modal PASS; watch action height 40 px. Owner-assisted Vimeo popup immediate PL→EN, real Options link and confirmed reset→Auto PASS (owner response: “Działa”). Shared language, context-menu rebuilding, open-modal relocalization, preference persistence and generic hidden controls also have automated regression coverage. Browser Auto in both UI locales, physical phone certificate trust, native context-menu clicks and the full closed-app handoff matrix were **NOT EXECUTED in this round**; do not infer them from unit tests.

Clean framework checkout still matches freshly fetched `origin/master`: `9214c8f5cec66e3731e5bf118cc8bfd5a9fba623`. Public-repository privacy scan PASS with no findings. Application branch remains `master`, HEAD `5d3f4ac8163739b727203d87f6fd3e5c8546f226`, index empty; earlier local changes preserved.

### Local artifact

- `artifacts/release/ModernTubeDownloader-1.1.0-candidate.2-win-x64.zip`
- Windows x64 **self-contained, multi-file, untrimmed**; 60,644,845 bytes, 374 entries. No bundled downloader tools, tests, user data or normal-package PDBs; symbols are separate.
- SHA-256: `4520e238e5523b5346d7f03940c9bb739f8d1a6b1c6b66ee6cbbef8b47f90a4f`.
- Build/publish and Debug native QA PASS; a separate launch of this candidate.2 self-contained EXE was not performed. Historical candidate.1 EXE evidence below is not current candidate.2 evidence.

### Files touched by this round

These extend an already dirty candidate.1 tree; this is not a list of every pre-existing local change.

- Framework/version pins: `Directory.Build.props`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `ModernTubeDownloader.Tests/ReleaseReadinessTests.cs`.
- Pairing/desktop presentation: `ModernTubeDownloader/WebRemote/WebRemotePairing.cs`, `WebRemoteHost.cs`, `ModernTubeDownloader/Views/SettingsView.cs`, `ModernTubeDownloader/Resources/Languages/pl.json`, `en.json`.
- Remote page: `ModernTubeDownloader/WebRemote/wwwroot/app.js`, `i18n.js`, `index.html`.
- Extension: `browser-extension/manifest.json`, `_locales/{pl,en}/messages.json`, `src/i18n.js`, `background.js`, `content.js`, `modal.js`, `popup.html`, `popup.js`, `styles.css`; new `src/options.html`, `options.js`, `options.css`.
- Regression tests: `ModernTubeDownloader.Tests/WebRemotePairingTests.cs`, `WebRemoteSecurityTests.cs`, `WebRemoteLocalization.test.cjs`, `WebRemoteTimeInput.test.cjs`, `AutomationTestHostTests.cs`, `browser-extension/tests/extension.test.cjs`; native harness `scripts/Test-Automation.ps1`.
- Documentation: this report, `WEB_REMOTE.md`, `browser-extension/README.md`, `ARCHITECTURE.md`, `AUTOMATION_TESTING.md`, `README.md`, `SUPPORTED_SOURCES.md`, `THIRD_PARTY_NOTICES.md`, `CURRENT_STATUS.md`, `CHANGELOG.md`, `RELEASE_CHECKLIST.md`.

### Acceptance summary

- **PASS:** full scoped builds/tests/publish, fresh authentication boundaries, HTTP/HTTPS integration, real Chrome remote/YouTube and owner-assisted Vimeo flows, native scroll regression.
- **FIXED:** ambiguous pairing-code states, missing automatic code on enable/save, Follow app synchronization, extension shared language/Options/reset, redundant polling view rebuild.
- **PARTIAL:** original reported browser session is historically unknown; security is established on fresh clients, not retrospectively guessed. Options theme/responsive rules have code coverage/inspection but no exhaustive browser/device visual matrix.
- **NOT EXECUTED:** physical phone/certificate trust, real Chrome UI-locale Auto matrix and OS context-menu/closed-app transport matrix, separate candidate.2 published-EXE launch.
- **KNOWN LIMITATIONS:** extension has no desktop-language return channel; HTTPS trust is manual; SponsorBlock Remove remains Experimental/default OFF. No global firewall/trust changes.
- **RECOMMENDATION: READY FOR 1.1.0 FINAL ACCEPTANCE within this scoped candidate evidence**, not authorization to publish. No commit, push, tag, release, store or NuGet publication performed.

---

## Historical candidate.1 evidence

Date: 2026-10-07. Local `1.1.0-candidate.1`, Unreleased. No commit, push, tag, GitHub Release, NuGet or browser-store publication. Public v1.0.0 and its historical evidence are unchanged.

## ModernFormsNext

- Previous: `f521f9dfcfe601bf9b6199b88132cccb2380d1bf`.
- Current: `cc00609880cdb2f7d0351ee7e69d2d3badd91698`, matching fetched `origin/master`.
- Two intervening commits: `4a3c14e` software-rendering backend extraction and `cc00609` PR #171 merge.
- Development uses ProjectReference to the clean, ignored, detached `.mfn-master-worktree`; central pin and both CI/release checkouts match. No copied framework sources, experimental branch or MFN patch.
- Audited Form/Application, Testing, window/rendering lifecycle and default Auto software backend; no compatibility workaround needed. Native UI and actual self-contained EXE were exercised on this revision.

## Web Remote localization

One PL/EN resource set covers static labels, queue/LIVE statuses/actions, pairing/errors, units and time editor. Auto follows the first preferred browser language (pl-* → PL, others → EN). Browser-local override persists only a language value in localStorage and applies without restart. Request language headers localize DTOs without changing shared desktop locale; stale in-flight localization responses are discarded.

Real Chrome: initial Auto selected PL; changed PL → EN → PL; English override survived reload. Login instructions and invalid-code error translated. Dashboard, completed items, LIVE headings, options and numeric 2–10 second editor were exercised. No horizontal overflow at 320/390 px. The disconnect action is hidden until paired. Phone keyboard/device behavior is not re-qualified by desktop viewport emulation.

## Pairing / authentication

- Crypto-random 8-character alphabet `23456789ABCDEFGHJKLMNPQRSTUVWXYZ` (32 symbols, 40 bits), displayed ABCD-EFGH; ten-minute TTL.
- Single successful exchange consumes the code. Five attempts/minute/client, eight/code across clients; capped client/session dictionaries. New code preserves existing sessions and client attempt limits.
- New independent 256-bit opaque session; server hash lookup. HttpOnly/SameSite=Strict cookie, Secure on HTTPS, absolute eight-hour expiry.
- Logout, listener stop/restart and auth-mode change revoke sessions. All credentials are volatile; no QR, URL, application-log or storage persistence.
- Browser contains no bearer token and cannot read its session cookie. Legacy high-entropy diagnostic bearer support remains for compatibility, never exposed by browser UI.
- Pairing and cookie/no-auth mutations require same-origin plus X-MTD-Request. Existing Host/Origin, CSP, no-store, private-IP binding, rate/body/concurrency limits remain.
- QR contains only the current HTTP/HTTPS address and is discarded when that address changes. Auth-disabled UI displays a warning.
- Real HTTP and HTTPS listener tests cover successful exchange, cookie attributes, repeated code rejection, CSRF, logout, mode changes and restart. Unit tests cover expiry, concurrency, attempts and session TTL. Physical phone pairing is NOT EXECUTED this round.

## Optional HTTPS

Implemented: YES, explicit opt-in Kestrel HTTPS; default stays HTTP with a clear unencrypted-LAN warning.

Explicit generate/regenerate uses one-year self-signed RSA-2048/SHA-256 certificate with actual selected IP SAN and server-authentication EKU. Changing bind IP requires regeneration; missing/corrupt/mismatched/expired credentials fail clearly, without silent HTTP downgrade. Save bind address before generating. Public-only export and SHA-256 fingerprint are available in Settings.

Private PFX bytes are encrypted using per-user DPAPI; DataProtection keys are DPAPI protected too. Restricted inherited ACL grants current user and LocalSystem. Clear PFX buffers are memory-only and zeroed. Windows Schannel requires a transient per-user key container (UserKeySet without PersistKeySet), disposed with the listener certificate. An initial EphemeralKeySet attempt caused a real TLS EOF failure and was corrected; TLS now genuinely passes rather than accepting an HTTP-only mock.

Tests verify exact certificate fingerprint, IP name match, untrusted-chain rejection, Secure cookie, protected storage/ACL, regeneration and public export without a private key. No trust-store or firewall mutation occurred. Self-signed trust on phones/Chrome remains a deliberate device-specific procedure, not automatic HTTPS qualification. [Kestrel documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0).

## Extension localization and universal handoff

Chrome i18n `_locales/pl` / `en`, default English; manifest name/description, one popup HTML, injected modal, context menus/preferences/status/errors. Chrome UI locale determines PL/EN, independently of the desktop.

- Toolbar popup accepts safe HTTP/HTTPS URLs; Analyze is primary, QuickQueue explicit.
- Page/link context menus accept HTTP/HTTPS and default to Analyze; optional QuickQueue default is a local extension preference.
- Independent injected-button/context-menu switches. Existing last-quality preference is retained for YouTube only.
- New bounded v2 Analyze / QuickQueue / Open contract; backward-compatible v1 remains YouTube-only. Mixed/unknown fields, bad flags, duplicate keys, unsafe schemes, credentials, malformed escapes and raw command/path fields are rejected.
- Desktop canonical SourceCheck/yt-dlp determines support and media kind. Generic QuickQueue normalizes to Best/Auto. Generic playlists go to existing selection; active/upcoming LIVE requires existing desktop confirmation, never automatic capture. No new extractor/queue/LIVE subsystem.
- No cookies/history/download permissions, broad host permission or all-site content script. Existing activeTab/contextMenus/storage and YouTube content matches only.
- Protocol association/current-user single-instance IPC remain existing production transport; no terminal-launched helper requirement. The protocol has no response channel and cannot promise queue acceptance or bypass the browser's external-app prompt.

## Adapters and sources

Existing YouTube DOM anchors were extracted into one adapter. Watch/Shorts/playlist/LIVE sizing and SPA lifecycle remain; no unverified extra adapters. Generic transport does not require DOM scraping for every yt-dlp extractor and does not promote all sites to Verified.

Real native canonical metadata checks used yt-dlp 2026.08.19 (1751 extractors), FFmpeg and Deno from an existing isolated QA tools profile:

| Source | Observed result | Scope |
| --- | --- | --- |
| YouTube Sintel, eRsGyueVLvQ | Public VOD, 47 formats, subtitles/chapters | Metadata/preview PASS; this round did not redownload |
| Vimeo 1084537 | Localized authentication/cookies guidance | Error flow PASS, extraction PARTIAL; no login/cookies supplied |
| Archive BigBuckBunny_328 | VOD, three formats, missing codec facts explained | Metadata PASS; no new download qualification |
| Twitch WildLifeCam | Actual is_live, six formats, TwitchStream | Metadata PASS; no new capture started |

Metadata-only checks assert empty ordinary/LIVE queues. The Web Remote YouTube HTTPS add allowlist remains narrower and unchanged (not broadened by universal desktop handoff).

## Real Chrome / native QA

- Chrome updated unpacked extension: watch modal PL, Shorts modal, playlist modal, one button, 40 px watch action matching native heights, 920 px centered modal without overflow. Playlist → watch SPA navigation retained one button and current title. Merely opening/cancelling did not launch protocol.
- User confirmed actual Vimeo toolbar popup with separate Analyze / Add to queue and preferences, then supplied screenshots.
- Screenshots revealed CSS display:block/flex overriding hidden: generic quality label/hint remained visible; the same issue could expose hidden LIVE QuickQueue. Added popup-scoped [hidden] display:none !important and regression. User reopened popup and confirmed correction with a final screenshot. YouTube styling is unaffected.
- Real EN Chrome UI / full context-menu, closed-app and prompt matrix: NOT EXECUTED. Browser tooling blocks chrome-extension: pages; no workaround or permission escalation was used.
- Remote Chrome used a real loopback Kestrel test fixture; auth-on login/invalid code plus explicitly auth-off isolated dashboard. Language/range/mobile checks PASS. Complete successful browser pairing/logout is qualified by HTTP tests, not claimed as a physical-phone/manual-browser flow.
- Native EN/Dark and PL/Light common Automation: normal start without bridge, Debug opt-in bridge, preview/dropdown, advanced options, Details tabs and close, Settings/persistence, clean shutdown PASS.
- Native Settings scroll: 20 pairing-checkbox toggles per EN/Dark and PL/Light while scrolled, preserved offset/content alignment, exact 80 px code+expiry-row extent delta, wheel/track/thumb checks PASS. Assertions were adapted to the new two-row content, not removed.
- Self-contained published EXE: normal native UIA start, actual Sintel analysis, Downloads/Settings/LIVE navigation and clean close PASS. No download or capture requested.

## Validation

- Restore, Debug and Release builds: PASS, zero compiler warnings/errors.
- Full .NET: 401/401 Debug and 401/401 Release, no skipped tests.
- JavaScript: 49/49 (21 extension + 28 Web Remote), no skipped tests; PL/EN consumed-key parity included.
- Real HTTP/HTTPS security and certificate tests are included above.
- Native common smoke and Settings regressions: PASS for both EN/Dark and PL/Light.
- Real-source metadata smoke and self-contained native smoke: PASS within the scopes above.
- Build-Release: self-contained win-x64, multi-file, untrimmed; app/runtime/resources/licenses, no media tools, tests, Automation assemblies, private certificates, runtime data or PDB in ordinary ZIP. Symbols are separate.
- Repository/history hygiene: PASS, zero findings; no generated QA assets are added to source.
- Logs/TRX/proofs live in ignored artifacts or isolated Temp profiles. Main log: `artifacts/browser-remote-final-validation.log`; source/native evidence: `browser-remote-real-sources-final.log`, `browser-remote-release-native.log`, `browser-remote-automation-*.log`.
- During iteration: two outdated contract tests failed (old token ID/44 px extent and pre-v2 generic rejection), then updated to explicit new contracts with security/layout assertions retained. A concurrent testhost locked a DLL during a rebuild; stopped shared-output overlap and reran serially. One initial real-source startup exited before HWND; bounded immediate diagnostic and subsequent real-source/EXE smoke passed. No product root cause is asserted for that isolated startup result.

## Local artifact

`artifacts/release/ModernTubeDownloader-1.1.0-candidate.1-win-x64.zip`

Self-contained, local candidate only. Exact bytes/SHA-256 are in the adjacent `.sha256` and final build log. The v1.0.0 ZIP was retained; generated publish/release directories were regenerated by the scoped packaging script. No artifact is uploaded.

Final package: 60,636,468 bytes (57.827 MiB), 374 entries; zero forbidden entries (including private certificate/PFX/PEM, runtime data, PDB, Automation/tests and downloaded tools). SHA-256: `5a989083aefa7614a23a3719fc11aae89f2da088b3864ddf0499d5e258922d5a`. Final full TRX: `artifacts/validation/20261007-184726-eeac47d0/Debug/release-validation.trx` and corresponding `Release` directory. Repository remains master at `5d3f4ac8163739b727203d87f6fd3e5c8546f226`, with only local unstaged work and an empty index.

## Changed files

```text
.github/workflows/ci.yml
.github/workflows/release.yml
ARCHITECTURE.md
AUTOMATION_TESTING.md
BROWSER_REMOTE_1_1_IMPLEMENTATION.md
CHANGELOG.md
CURRENT_STATUS.md
Directory.Build.props
ModernTubeDownloader.Tests/AutomationTestHostTests.cs
ModernTubeDownloader.Tests/ExternalLinkTests.cs
ModernTubeDownloader.Tests/ExternalMediaPolicyTests.cs
ModernTubeDownloader.Tests/ReleaseReadinessTests.cs
ModernTubeDownloader.Tests/SupportedSourcesTests.cs
ModernTubeDownloader.Tests/WebRemoteIntegrationTests.cs
ModernTubeDownloader.Tests/WebRemoteLocalization.test.cjs
ModernTubeDownloader.Tests/WebRemotePairingTests.cs
ModernTubeDownloader.Tests/WebRemoteSecurityTests.cs
ModernTubeDownloader.Tests/WebRemoteTimeInput.test.cjs
ModernTubeDownloader/Infrastructure/ExternalMediaRequest.cs
ModernTubeDownloader/MainForm.cs
ModernTubeDownloader/Resources/Languages/en.json
ModernTubeDownloader/Resources/Languages/pl.json
ModernTubeDownloader/Services/ExternalMediaPolicy.cs
ModernTubeDownloader/Settings/AppSettings.cs
ModernTubeDownloader/Views/DownloadsView.cs
ModernTubeDownloader/Views/SettingsView.cs
ModernTubeDownloader/WebRemote/WebRemoteCertificateStore.cs
ModernTubeDownloader/WebRemote/WebRemoteHost.cs
ModernTubeDownloader/WebRemote/WebRemotePairing.cs
ModernTubeDownloader/WebRemote/wwwroot/app.css
ModernTubeDownloader/WebRemote/wwwroot/app.js
ModernTubeDownloader/WebRemote/wwwroot/i18n.js
ModernTubeDownloader/WebRemote/wwwroot/index.html
README.md
RELEASE_CHECKLIST.md
SUPPORTED_SOURCES.md
THIRD_PARTY_NOTICES.md
WEB_REMOTE.md
browser-extension/README.md
browser-extension/_locales/en/messages.json
browser-extension/_locales/pl/messages.json
browser-extension/manifest.json
browser-extension/src/adapters.js
browser-extension/src/background.js
browser-extension/src/content.js
browser-extension/src/i18n.js
browser-extension/src/modal.js
browser-extension/src/popup.html
browser-extension/src/popup.js
browser-extension/src/shared.js
browser-extension/src/styles.css
browser-extension/tests/extension.test.cjs
scripts/Build-Release.ps1
scripts/Test-Automation.ps1
scripts/Test-PublicRepository.cjs
```

## PASS

Implementation, exact MFN pin, bounded source-neutral handoff, PL/EN resources, short-code/opaque-session auth, real Kestrel TLS/security, native regressions, current candidate build/test/package and scoped Chrome/user popup checks.

## PARTIAL

Vimeo requires authentication; generic extractor presence is not platform-wide verification. Self-signed HTTPS is technically validated but device trust UX is unqualified. Chrome popup confirmation is user evidence; full OS-menu/closed-app flow is not independently complete.

## NOT EXECUTED

Physical phone/LAN pairing this round; trusted phone/Chrome certificate installation; full real English Chrome/context-menu/OS prompt matrix; renewed real downloads/long LIVE recovery/A-V playback; GitHub CI for an uncommitted head. No credentials, root trust or firewall changes were made for these checks.

## Known limitations / recommendation for 1.1.0

Keep this as local 1.1.0 candidate. Finish targeted browser/device acceptance and explicit owner review before stable publication. Generic browser handoff is implemented, not an all-extractor guarantee; in-page integration stays YouTube-only. HTTPS requires conscious trust setup or explicit HTTP use on a trusted LAN. Existing SponsorBlock Remove remains Experimental/default OFF with its previously confirmed playback defect. No unrelated downloader/LIVE redesign or release action was performed.
