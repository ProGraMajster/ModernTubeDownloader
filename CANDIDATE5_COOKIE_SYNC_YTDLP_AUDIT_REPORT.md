# ModernTubeDownloader 1.1.0-candidate.5 — cookies and yt-dlp audit

Date: 2026-10-10. Local candidate only. `master` remains at `5d3f4ac8163739b727203d87f6fd3e5c8546f226`; pre-existing candidate changes were preserved and the index is empty. No commit, push, tag, GitHub Release, Chrome Web Store or NuGet publication. v1.0.0 remains unchanged.

**Recommendation: NOT READY for the complete requested cookie-sync acceptance / unqualified stable release.** The opt-in synchronization, canonical credential path, synthetic real-Chrome flow and audit are available for review. Desktop-initiated browser refresh after authentication failure is still not implemented; qualification limits and a non-reproduced cleanup failure are retained below.

## MODERNFORMSNEXT

- Previous and final SHA: `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`.
- Fresh `git fetch origin` confirmed HEAD = `origin/master`; `.mfn-master-worktree` is clean/detached. Development/CI/release consume the same central `Directory.Build.props` pin. No framework source edits.
- Existing official native automation and Windows notifications remain in use; no duplicate framework API or semantic tree.

## COOKIE ARCHITECTURE

`browser-extension/src/cookies.js` owns credential reads in the worker. `cookies-options.js` owns explicit consent/metadata-only selection. `cookies-client.js` gates popup/content handoff on a snapshot ACK; context-menu failure uses an explicit continuation page. Existing media URI/queue/LIVE behavior is not replaced.

`ModernTubeDownloader.Authentication` owns bounded contracts, `CookieStore`, Netscape leases, native registration/authority/framing. `ModernTubeDownloader.NativeHost` is a separate one-shot process with only the authentication dependency. No UI, SingleInstance MainForm, queue worker, LIVE scheduler, ToolManager or Web Remote is started by cookie events.

`AppServices` creates the canonical `YtDlpAuthenticationProvider`. `YtDlpProcessRunner` inserts one `--cookies FILE` before `-- URL`, sanitizes callbacks and captured results before error creation, and rejects double ownership. Manual files remain user-owned and mutually exclusive with synchronized profiles. Old `UseCookieFile` settings migrate without silently combining sources.

## USER CONTROL

- None by default; explicit ManualFile or SyncedBrowserProfile in desktop Settings.
- Current service, selected service presets, explicit domains and Custom identities in extension Options. YouTube preset includes only youtube.com/subdomains, not google.com/accounts.google.com.
- Custom lists name/domain/path/session/expiration/Secure/HttpOnly/partitioned metadata, never value. No guessed stable “minimal login” cookie names or extractor-sized preset list.
- Before-use/change/startup ON only after consent; periodic OFF, bounded 5–1440 min. Change events coalesce through alarms; startup restores configured alarms. Chrome production alarm scheduling can differ from unpacked mode.
- Explicit Sync now, revoke/delete and edit. Active attempt jars cannot be retroactively recalled from a running yt-dlp process; future attempts stop using revoked data.
- Failed deletion remains visibly pending. Disabled pending profiles retain Native Messaging until all tombstones are acknowledged. Retrying deletion can request only the native permission, not cookies or host origins; no automatic full-browser data grant.

## NATIVE MESSAGING

Host: `com.programajster.moderntubedownloader.cookies`. User supplies exact extension ID/browser; no final hardcoded dev/store ID or wildcard origin. HKCU manifests contain an absolute standalone EXE and only registered origins. Chrome/Edge registration is implemented; Brave/Chromium are experimental/unqualified. Moving the installation requires explicit repair.

Version 1: four-byte little-endian length + UTF-8 JSON, maximum 512 KiB, JSON depth 12, unknown fields rejected, 20-second request deadline. Browser/origin/process ancestry checked before credential input. Shell-parent negative test exits 2 with empty stdout/stderr, not a MainForm. Browser vendor metadata is not cryptographic attestation.

Limits: 1,000 cookies, 16 profiles including replay tombstones, 16 scopes, 8 origin/browser registrations; name 256, value 8,192, path 2,048, domain 253. Reject CR/LF/TAB/control injection, bad timestamps/expiry/domain/store, outside-scope records, duplicates and stale snapshot versions. Partitioned cookies are excluded/count-only rather than flattened.

### Actual registration root cause and fix

Real Chrome initially reported host-not-found. Chrome's own host log said the manifest file did not exist, although the tooling could read its logical path. An identical public manifest in an ordinary folder reached .NET, but the host could not see Auth. File-handle physical-path inspection showed inherited MSIX AppData redirection into the tool package's private LocalCache; the same logical path/user SID did not imply the same file.

A helper manually launched by the owner outside that inherited context installed only the approved public binding/manifest, with user-only ACL, for the test ID. Real Chrome then ACKed the empty snapshot and later the synthetic change. No browser policy, ACL widening, firewall or account-domain access was used.

Product registration now checks both physical directory and a zero-byte delete-on-close file-creation route before writing bindings/HKCU; mismatched routes fail with localized instructions to launch the normal desktop EXE from Explorer. This portable Win32 candidate does not promise MSIX/package deployment. See [architecture/primary references](COOKIE_SYNC.md#redirected-appdata-real-qa-root-cause).

### Actual revoke follow-up

Owner screenshots confirmed that disabling was not initially acknowledged. Code review found premature global permission teardown could deprive other disabled/pending profiles of native retry authority. Regression coverage now retains transport until all pending tombstones are ACKed and avoids calling alarm APIs when that permission is absent. Counts clear only after successful deletion.

After optional Native Messaging was regranted, real Chrome's existing worker still lacked `sendNativeMessage`; one extension/Options reload restored it and the owner confirmed deletion. UI now explains this condition. This evidence does not establish that every Chrome version dynamically updates optional API bindings without reload. There is no silent extension/page reload loop.

## SECURITY

- Whole profile document and tombstones use DPAPI CurrentUser + fixed purpose entropy, protected ACL and cross-process bounded mutex. Writes stage encrypted bytes only, then atomically replace; no plaintext JSON backup.
- Corrupt/unreadable stores fail closed and are not overwritten by normal sync. Explicit desktop Delete allows recovery, clears data and blocks future snapshot writes until renewed desktop registration consent.
- Matching uses host-label boundaries and host-only semantics. Exactly one deterministic profile wins by scope specificity/newest snapshot/stable ID; never merge unrelated credentials.
- Temporary Netscape jars have random names/user-only ACL/exclusive ownership markers; same-attempt stages retain their jar, concurrent attempts use separate jars, new snapshots never overwrite active files. Cleanup ignores active locks and removes abandoned owned jars.
- Known cookie values are redacted before callbacks/results/errors/logs/crash handling. Credential-bearing metadata fields are recursively removed; known values in other strings/arrays are scrubbed. Manual file path and generated jar paths are registered for redaction. No raw payload/value in UI, Chrome storage, Native ACK, URI, argv values, Web Remote, clipboard or reports.
- Reserved raw cookie/credential/alias/netrc/command/plugin/downloader/postprocessor/debug-traffic options and credential headers include tested abbreviations/inline forms. This is **not** comprehensive certification of all optparse short clusters, extractor arguments or every custom networking switch; expert argument hardening remains a P0 follow-up.
- Same-user malware/debuggers can access decrypted memory/DPAPI; immutable managed strings and filesystem deletion cannot provide forensic zeroization. Short cookie values may over-redact unrelated metadata strings. Unknown future upstream secrets are not guaranteed identifiable.
- Scope validator is not a full maintained PSL; exotic public suffix policy needs hardening before a comprehensive claim. HTTPS domain permission does not prove a session is valid. Netscape loses SameSite/partition/store distinctions; partitioned data is deliberately excluded.

## YT-DLP

Managed version **2026.08.19**, binary SHA-256 `66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a`. Upstream audit SHA `51bab8a0116f4d8004c315706d809782607d5847` is distinct, not a claim that the installed binary is the newest release.

Actual parser test rerun with [Test-CookieJar.ps1](scripts/Test-CookieJar.ps1): real managed yt-dlp loads and rewrites a generated Netscape jar on public example.com with a synthetic cookie. LF, seven columns, HttpOnly marker, session expiry 0, path-with-spaces handling, no parser warning/value output and final cleanup PASS. Unsupported media URL/exit 1 is expected; this tests real jar load/save, **not an authenticated download**.

| Existing path | Canonical integration / ownership |
|---|---|
| Single-video metadata and format discovery | YtDlpMetadataService → central runner → standalone operation jar |
| Flat playlist and per-entry analysis | Same metadata service/runner; selected item worker resolves its own actual URL |
| VOD media/ranges/SponsorBlock | DownloadJobExecutor opens logical attempt; all selected stream invocations share its jar |
| Subtitle discovery/download | Metadata + SubtitleDownloadService use the same runner/session |
| Retry/reanalysis | New executor attempt takes newest store; never modifies an active prior jar |
| SourceCheckService | Real extraction uses metadata service/runner, not a cookie-free alternate process |
| LIVE availability/upcoming polling | Each probe uses metadata service/current store rather than enqueue-time snapshot |
| Active LIVE metadata/record/reconnect | LiveRecordingExecutor owns each attempt; next reconnect acquires newest snapshot |
| Replay/VOD, extension Analyze/QuickQueue and external Open | Existing classification/queue services lead to the same metadata/executor path |
| Web Remote add | Existing allowed YouTube source path uses desktop auth policy; API receives no cookies |
| Tool version/catalog/help calls | No source extraction or cookies required; deliberately not credential-bearing |

## LIVE

Scheduled sessions do not persist a frozen credential snapshot. Current encrypted profile is selected at availability/start and reconnect boundaries; closed browser permits the last received nonrevoked snapshot. Separate existing LIVE concurrency, FromNow, Stop/save, waiting/persistence, recovery parts, Partial and resumable VOD remain untouched. Synthetic regression covers these services, not a new authenticated broadcast qualification.

**Desktop-initiated JIT refresh after auth failure is incomplete (request §36).** One-shot Native Messaging has no active reverse request/ACK contract. Extension-before-handoff JIT works; app retry uses latest already received store, but auth/private failures remain permanent/manual sync + explicit retry. Do not display “refreshed from Chrome” or fabricate an ACK. A consent-bound reverse channel and one bounded retry need separate implementation/security qualification within the remaining scope.

## TESTS AND VALIDATION

Final full [Build-Release.ps1](scripts/Build-Release.ps1) run: `artifacts/validation/20261010-162346-393c6b19/`.

| Check | Result |
|---|---|
| Restore + Debug build, warnings as errors | PASS, 0 warnings/errors |
| Full Debug | **491/491**, 0 failed/skipped |
| Release build, warnings as errors | PASS, 0 warnings/errors |
| Full Release | **491/491**, 0 failed/skipped |
| CookieSecurity subset, explicit Debug rerun | **49/49** (included in 491, not extra) |
| NativeHost subset, explicit Release rerun | **3/3** (included in 49/491) |
| Extension JS | **49/49** (30 prior + 19 cookies) |
| Web Remote JS | **31/31**, unchanged behavior |
| Combined JS | **80/80**, 0 failed/skipped |
| Desktop PL/EN catalog | 631/631 keys, 0 key mismatches; extension parity tests PASS |
| Real managed Netscape parser | PASS, synthetic public example.com, no account data |
| Native cookie Settings | PL/Dark and EN/Light smoke PASS, isolated profiles, no real bridge registration |
| Settings scrolling regression | EN/Dark PASS, 20 checkbox toggles while scrolled, wheel/track/thumb checks; existing token checkbox, not a cookie permission change |
| Ordinary native range/Details/Settings | EN/Dark PASS with synthetic QA metadata; 2–10 s range persists, all Details tabs open, close unregisters its root; not a real download |
| Published normal startup | PASS, isolated profile, ready transition/clean close/no Automation bridge; custom QA tools, **not real provisioning/download** |
| Publish/package | PASS, self-contained win-x64 GUI + separate NativeHost, untrimmed multi-file |
| Bounded repository/publication audit | PASS after removing the developer-local ZIP path from this report; 227 candidate files and 265 historical blobs checked, 0 findings; not universal secret detection |

Cookie Settings captures were inspected in both tested language/theme combinations. Initial captures showed only the source selector and existing manual-cookie area. The capture-only harness now scrolls the canonical CookieSyncSection into view; fresh PL/Dark and EN/Light runs show readable source/privacy/status, refresh/delete, browser/extension-ID, consent and register/options controls without overlap. This is scoped normal-window inspection, not qualification of every state, resize or DPI combination. No registration or credential reads are performed by this UI smoke.

An earlier complete run passed 489 tests per configuration before the last two route-probe regressions. The first 491-test Debug run failed **one** pre-existing thumbnail fixture cleanup: `ThumbnailCacheServiceTests.MissingInvalidOrOversizeNotificationCacheDegradesWithoutNetwork("invalid")`, `IOException` deleting an in-use `.img` in Dispose at line 122. The focused three-case rerun and fresh complete final pipeline passed. No test, retry threshold, assertion, skip or thumbnail code was changed to mask it. Exact failing TRX: `artifacts/validation/20261010-162146-c4504aaf/Debug/release-validation.trx`. Root cause is **not established**; a passing rerun does not prove it fixed. The earlier publish also stopped on the DPAPI package lacking a standalone LICENSE; exact MIT package attribution/notices + required full runtime license are now preserved, not silently omitted.

### Real browser evidence and cleanup

Chrome test used only the specifically approved example.com scope and dev ID. Empty snapshot ACK was observed by the owner with a screenshot. Owner then confirmed automatic change sync without Sync now for one synthetic cookie and removal of that browser cookie. Subsequent screenshots showed one-record profile metadata (no values). Deletion was initially pending, then acknowledged after permission-lifetime fix/worker reload.

Owner ran the fail-closed unpackaged cleanup helper. Result: `Cleaned=true`, `ProfilesRevoked=3`, `RegistryRemoved=true`, `FilesRemoved=true` in ignored `artifacts/validation/candidate5/unpackaged-cleanup.json`. It checked only these three approved-origin revoked/zero-cookie entries in memory and removed exact generated store/binding/manifest/health files + the matching HKCU host key. No browser database/account cookies or unrelated application data were removed. Test metadata/tombstones and ignored QA artifacts are not product assets. Temporary NativeHost debug instrumentation was removed from source before final builds.

## PACKAGE

- ZIP (repository-relative): `artifacts/release/ModernTubeDownloader-1.1.0-candidate.5-win-x64.zip`.
- **106,529,646 bytes**, **611 entries**.
- SHA-256: `0c52878fdf8390a6c87749bea926773778c9e8149215aea8907a2837cf3df3ae`.
- Separate symbols ZIP; no PDBs in user ZIP. Main GUI + native-host standalone runtime explain size increase from candidate.4.
- No Auth/store/jar/browser profile/runtime settings/logs/QA snapshots/test executables/automation assemblies/media tools/user-specific native manifest/registry export. COOKIE_SYNC, app/runtime licenses and exact ProtectedData notices included. Package checker remains fatal on forbidden/missing entries.

## YTDLP GAP AUDIT

[YTDLP_FEATURE_GAP_AUDIT.md](YTDLP_FEATURE_GAP_AUDIT.md) contains all 17 captured categories, 11-column grouped matrices, code/test evidence and future Basic/Advanced/Expert placement. Coverage: **250/250 distinct long-option names, zero unnamed; 100 grouped rows**.

| A | B | C | D | E | F | G | H | I | J |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 10 | 22 | 3 | 0 | 13 | 8 | 18 | 8 | 13 | 5 |

Counts are grouped features, not percentages of switches “implemented”. App retries/ranges/playlists/subtitles/SponsorBlock/containers/LIVE/manual cookies/catalog/custom arguments were checked rather than marked absent. No D row identified in this matrix is not a whole-UI quality guarantee.

## PASS

Canonical exclusive auth layer; bounded DPAPI/native contract; attempt ownership/revocation/redaction and process tests; real synthetic Chrome snapshot/change/delete and physical cleanup; managed Netscape parser; final complete .NET/JS/publish/package pipeline; selected-domain scope only.

## FIXED

Native lastError callback handling; snapshot/revoke version races; pending multi-profile permission lifetime; API-less deletion retry guidance; registration physical-route mismatch; explicit corrupt-store deletion recovery; reserved credential headers/options; DPAPI redistribution attribution gate.

## PARTIAL

Desktop auth-failure reverse refresh/bounded retry; direct-ACK freshness is not session validity; worker reload required after optional API regrant on observed Chrome; replay-safe tombstone compaction/profile edits; full PSL; comprehensive expert-argument security; overall stable release qualification.

## NOT EXECUTED

Real authenticated account/download/playlist/LIVE/subtitle sessions (no such consent or credentials supplied); real Edge/Brave/Chromium native flow; installed-store ID/review; clean-PC native host installation/missing-runtime matrix; exhaustive source/OS/DPI/device/multi-hour authenticated soak. Native action/cold activation limits from candidate.4 remain separate. Historical LIVE/download/phone/audio evidence is not relabeled as candidate.5 auth qualification.

## SECURITY LIMITATIONS

Same-user access and non-forensic deletion; immutable strings/unknown upstream secret discovery; short-value over-redaction; incomplete PSL; no Netscape partition/SameSite fidelity; vendor metadata authority is not cryptographic attestation; unqualified expert argument combinations; no authenticated session validity guarantee. See [COOKIE_SYNC.md](COOKIE_SYNC.md).

## TOP YT-DLP NEXT FEATURES

1. Durable success-only download archive.
2. Typed bandwidth cap.
3. Bounded fragment concurrency, separate from app slots.
4. Explicit audio encoding/quality.
5. Codec/FPS/HDR selection criteria.
6. Selected metadata/artwork embedding.
7. Date filters and playlist index expressions.
8. Chapter selection/export.
9. Validated small extractor profiles.
10. Credential-separated network/proxy profiles.

Complexity, dependencies, risk and required tests are in the audit. These are proposals, not implemented features.

## REMAINING 1.1.0 BLOCKERS

- P0: finish consent-bound desktop auth-failure refresh/ACK + bounded retry; harden remaining expert-argument credential bypass edges before claiming complete protection.
- Diagnose the intermittent thumbnail cleanup lock rather than call it fixed after a rerun; no mitigation was mixed into this cookie round.
- Qualify/explicitly accept browser/installed-ID/deployment limits; real authenticated qualification only with explicit owner consent.
- Retain existing Experimental SponsorBlock Remove defect and prior native action/cold/clean-PC limits; no stable quality claim.
- Explicit separate approval for stable promotion/publication. Nothing is committed or published.

## RECOMMENDATION

**NOT READY.** Reviewable local candidate.5 artifact and full feature audit are prepared; the complete requested auth-failure path and release gates remain open. No public release is proposed by this report.
