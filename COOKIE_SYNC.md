# Cookies and authentication — 1.1.0

Local, opt-in Windows implementation. This is credential material, not a convenience telemetry channel. Historical v1.0.0 is unchanged. See the [release checklist](RELEASE_CHECKLIST.md); the [candidate.5 report](CANDIDATE5_COOKIE_SYNC_YTDLP_AUDIT_REPORT.md) records the earlier baseline, not the final refresh implementation.

## Enable deliberately

1. Launch the unpacked/published desktop EXE normally from Explorer. In Settings → Cookies choose **Authentication: synced browser profile** and save. The default is **None**. Existing manual `cookies.txt` remains a separate, mutually exclusive source.
2. In extension Settings copy its public extension ID. In desktop Settings select Chrome/Edge, enter that exact ID, confirm registration and choose **Register / repair native bridge**. No admin rights or wildcard allowed origin are needed. Moving the installation requires registration again, because the host EXE path changes.
3. In extension Settings choose Current service, Selected services, Selected domains or Custom cookies. Inspect the displayed domains, timing and privacy notice, then explicitly grant permission. YouTube currently means **only youtube.com and its subdomains**, not google.com/accounts.google.com.
4. Enable and connect obtains optional cookies/nativeMessaging/alarms and only the selected HTTPS host origins. Sync now must return an ACK before the profile is shown as synced. Merely registering a host or granting permission is not a successful sync.

Custom displays name/domain/path, expiry/session, Secure/HttpOnly/partitioned metadata, **never values**. There is no guessed “minimal login cookie” allowlist or extractor-sized preset catalog. The service presets are a small convenience, not proof of authentication requirements.

## One credential path

```text
Selected Chrome cookies → extension service worker → Native Messaging frame
  → standalone NativeHost → DPAPI CurrentUser store
  → desktop YtDlpAuthenticationProvider → attempt-owned Netscape jar
  → central YtDlpProcessRunner --cookies FILE → existing yt-dlp flow
```

Values never travel in the custom URI, process command-line argument, Web Remote, clipboard, UI DTO, extension storage or logger. The `--cookies` argument contains a file path, not values; user-facing diagnostics redact that path. The desktop does not read browser databases or use `--cookies-from-browser`. No extra MainForm, scheduler, ToolManager or Web Remote host is started for cookie events. NativeHost has binary stdin/stdout only: one bounded snapshot/deletion request and metadata-only ACK, or an explicitly consented metadata-only refresh watch connection.

### Timing

- Before use, cookie changes and browser startup default ON **after consent**; periodic default OFF. An enabled period is 5–1440 minutes.
- Before Analyze, QuickQueue, Open and LIVE/playlist handoff, the worker obtains a matching snapshot, waits for native ACK, then invokes the existing media transport. Failure requires explicit continuation with the last available snapshot/no synced cookies. It never silently claims a refresh.
- Changes mark the profile dirty and replace one 5-second alarm; overlapping operations are coalesced. Installed Chrome alarms may fire no sooner than 30 seconds, unlike unpacked developer mode. Startup recreates configured alarms rather than assuming persistent worker memory. [Chrome alarms](https://developer.chrome.com/docs/extensions/reference/api/alarms)
- Optional permission events attach the API hooks when granted; removing permissions disables/revokes matching profiles. Browser shutdown does not delete the encrypted snapshot. No permanently polling desktop or extra localhost credential listener is used.

### Sources and freshness

`None`, `ManualFile`, `SyncedBrowserProfile` are exclusive. Legacy `UseCookieFile` settings migrate without silently mixing sources. Manual files are validated at save/use and remain user-owned: deletion/revocation of synced data does not remove a manual file.

Matching uses actual URL host boundaries: example.com includes subdomains, not evil-example.com. A host-only cookie remains host-only. One profile is chosen deterministically: most specific scope, newest snapshot, stable profile ID tie-break. Profiles are never merged. Status records browser, domains, count, last sync and monotonically increasing snapshot version. “Fresh” means synchronized within 30 minutes, **not a valid login**; expiry cannot establish that either.

## Native Messaging and installation

Extension Settings → **Delete profile** removes a profile from the visible list. Active profiles first use the existing revoke flow and require an acknowledged deletion. Pending/failed deletion remains visible and retryable; already acknowledged, zero-count profiles can be removed without regranting cookie/site/native permissions. Browser cookies are not deleted. The native store retains credential-free anti-replay tombstones so a stale snapshot cannot resurrect a removed profile.

Host: `com.programajster.moderntubedownloader.cookies`. Product: `native-host/ModernTubeDownloader.NativeHost.exe`, with a separate self-contained, untrimmed .NET runtime in the ZIP. Runtime duplication is intentional for a stable standalone process, not a single-file claim.

HKCU paths:

| Browser | Registry subkey before the host name | Qualification |
|---|---|---|
| Chrome | `Software\Google\Chrome\NativeMessagingHosts` | Real selected-domain ACK and owner-assisted change test |
| Edge | `Software\Microsoft\Edge\NativeMessagingHosts` | Implemented; real browser test not executed |
| Brave | `Software\BraveSoftware\Brave-Browser\NativeMessagingHosts` | Experimental, not manually qualified |
| Chromium | `Software\Chromium\NativeMessagingHosts` | Experimental, not manually qualified |

The per-user JSON manifest has an absolute EXE path and exact `chrome-extension://ID/` origins. Dev/store/browser IDs are supplied by the user, not hardcoded into the product. Store publication will require real store IDs, permission disclosure and review; an optional HTTPS wildcard declaration allows custom domains, but runtime grants are exact selected origins. Required permissions and YouTube content-script scope are unchanged. [Chrome Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging), [Edge Native Messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging)

NativeHost validates the declared origin **and** actual browser process ancestry before reading credentials. Browser executable basename/vendor metadata and at most one cmd launcher hop are checked. A shell caller with a copied public ID is rejected. This is a local authority check, not cryptographic browser attestation or a defense against malware already running as the same Windows user.

### Redirected AppData: real QA root cause

Registration from this desktop tooling inherited MSIX AppData virtualization: logical `%LOCALAPPDATA%\ModernTubeDownloader\Auth` writes landed under the tool package's `LocalCache\Local`. Chrome found the registry entry but logged `Failed to load manifest ... File doesn't exist`; an identical manifest placed in an ordinary folder reached the host, which then could not see the logical Auth directory. `GetFinalPathNameByHandle` identified the physical redirected file. A user-launched, unpackaged registration for the approved test ID fixed the real ACK; neither cookies permissions nor Auth ACLs were widened.

Registration now compares the physical Auth directory and the actual zero-byte delete-on-close file-creation route with their logical paths **before** updating bindings or HKCU. This also checks overlays where a real directory already exists but new files are redirected. Redirected/junction roots are rejected with PL/EN instructions to launch the desktop EXE from Explorer. This portable Win32 release does not claim packaged/MSIX deployment support. Do not point a host at a package-private credential store to hide the mismatch. [Microsoft AppData virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)

## Protocol and limits

Version 1, UTF-8 JSON preceded by a four-byte little-endian length. Snapshot fields: type, protocolVersion, profileId, browser, scopeMode, snapshotVersion, generatedAtUtc, scopes and cookies. No tab title, history, arbitrary URL/command or executable is part of this protocol. ACK contains only status/version/counts. Chrome transport failures are classified without echoing raw payloads or Chrome error text into UI. Callback `runtime.lastError` is consumed correctly.

| Boundary | Limit / rejection |
|---|---|
| Native input | 512 KiB, 20-second request timeout, JSON depth 12, unknown fields rejected |
| Profiles | 16 including tombstones; 8 allowed origin/browser registrations |
| Scopes | 1–16 unique normalized ASCII/punycode domains; IPs and broad known public suffixes rejected |
| Records | 1,000 cookies; store `0` only, not incognito stores |
| Strings | name 256, value 8,192, path 2,048, domain 253 characters |
| Records/time | CR/LF/TAB/control characters, impossible ordinary duplicates, outside-scope domains, invalid expiry/timestamps and stale versions rejected |
| Store | encrypted version 1 document, at most 9 MiB; cross-process bounded mutex and atomic encrypted replacement |

The domain validator does **not** contain a complete Public Suffix List. It rejects top-level labels and selected common multi-label suffixes, but exotic public suffixes can still be selected explicitly. Only explicit runtime permission grants permit reading. Harden with a maintained PSL before presenting this as comprehensive domain-policy validation.

Netscape does not encode partition identity. Partitioned cookies are excluded and counted, never flattened. SameSite is received/validated but cannot be represented in the jar; the browser session/store identity is not a portable general browser session model. Custom identity selection can include partitioned metadata, but the host still excludes those records. [Chrome cookies](https://developer.chrome.com/docs/extensions/reference/api/cookies), [actual yt-dlp cookie implementation](https://github.com/yt-dlp/yt-dlp/blob/master/yt_dlp/cookies.py)

## At-rest and attempt ownership

- `%LOCALAPPDATA%\ModernTubeDownloader\Auth\profiles.dpapi` encrypts the entire index with Windows DPAPI CurrentUser, including revoked tombstones. It has no plaintext JSON staging/backup. Atomic replacement writes only already encrypted bytes; input/serialized byte buffers are zeroed best-effort.
- The Auth directory and generated jars inherit **current-user-only ACLs**. DPAPI/ACLs do not defeat same-user malware or an already compromised authorized extension/tool.
- Auth `tmp` contains random GUID `.txt` jars plus exclusive `.lock` ownership files. A jar has a Netscape header, seven TAB-separated fields, LF/UTF-8 and `#HttpOnly_`; session expiry is zero. No cookie record is repaired silently.
- One logical VOD/LIVE attempt reuses its own jar across metadata/format resolution/download/subtitles so yt-dlp Set-Cookie changes survive. Concurrent jobs get distinct files. A newer browser snapshot never overwrites a running jar.
- Next retry/reanalysis boundary gets the latest store. Upcoming LIVE probes do not retain the snapshot from the time the session was scheduled; recording/reconnect obtains a fresh attempt lease. Existing VOD/LIVE schedulers, recovery and concurrency are unchanged.
- Dispose deletes best-effort; startup removes abandoned GUID jars only when their ownership lock can be acquired, including across processes. This is not forensic secure erasure. Plaintext must exist temporarily because yt-dlp consumes a file; trusted external tools can read it. Immutable managed strings cannot be reliably zeroed.
- Known values are scrubbed before callbacks, captured process results, errors/logging and metadata sidecars; metadata header/cookie/password/authorization fields are removed recursively. Avoid debug network dumps; reserved expert options prevent bypassing auth ownership. No absolute user paths or test stores belong in packages.

## Revocation and deletion

Extension **Disable sync and remove data** stops events/alarms first, waits for an in-flight snapshot, sends a newer tombstone and removes only permissions unused by other enabled profiles. Failed removal stays **Revocation pending**, not falsely “deleted”; it is retried at startup. Native transport is retained until all pending tombstones are ACKed. Explicit retry can request only Native Messaging without cookie/domain access; real Chrome required one extension/Options reload after regrant before its worker exposed the API. The UI explains that condition; there is no silent reload loop. Successful ACK clears displayed counts. Revoked metadata remains for monotonic replay protection and counts toward the bounded profile limit. Editing currently revokes the old profile and creates a new one; frequent edits can reach the limit. A compact/rebind design needs separate replay-safe qualification.

Desktop **Delete synced cookies** removes values, retains non-secret tombstones and blocks new snapshots until explicit bridge registration renews consent. It can explicitly reset a corrupt store; ordinary reads/sync still fail closed and never overwrite corruption automatically. The extension must also be revoked to stop its own granted read permission. Neither operation can retract a jar already handed to a running child process; existing attempts finish with their owned jar. No guarantee of recoverable/forensic deletion is made.

## Known qualification limits

Desktop-initiated refresh is implemented as described below. Real account authentication remains source/account dependent; synthetic transport and deterministic process tests do not certify an account login or every extractor.

Real Chrome qualification uses **only example.com**; no account/YouTube cookies or browser DB are read. Authenticated media, Edge/Brave/Chromium, incognito, Store installs, clean-machine permutations and exhaustive activation matrices remain post-1.1 qualification. Do not infer them from JS adapters or a parser test.

Troubleshooting: re-register after moving EXEs; verify browser-specific ID and declared origin; use localized status categories for missing registration/forbidden/exited/protocol. A bounded `bridge-health.json` contains only stage/type/time, no payload/message. Never attach jars, encrypted user stores, cookies or full network-debug logs to an issue. The temporary QA helpers/markers/screenshots stay in ignored artifact/output folders, not source or the ZIP.

## Authentication failure: consent-bound reverse refresh

An enabled profile with **Synchronize before use** consent owns a `connectNative` watch connection. This is not an HTTP cookie API, extra desktop service or public pipe. NativeHost checks browser ancestry and exact registered origin; its presence heartbeat expires after 15 seconds. Closing/revoking the last eligible profile closes the port. Reconnect uses Chrome alarms while consent remains active, not an automatic browser/page reload.

1. The canonical provider classifies a completed yt-dlp invocation as auth/session/cookie-related. Public operations and None/ManualFile never require this bridge.
2. For the selected JIT-consented profile it writes a nonce, profile/origin/browser, old version and deadline into the **existing DPAPI document**. Limits: 16 pending requests, 8 bridge presences; desktop waits at most 20 seconds.
3. NativeHost sends only that metadata over the watch port. The worker rechecks profile, consent and exact permissions, waits for any earlier sync, and obtains a **new** snapshot through the existing Native Messaging snapshot/ACK path.
4. Only an accepted snapshot with the matching unexpired nonce and `newVersion > oldVersion` proves success. An unrelated sync, process exit zero or stale ACK is insufficient.
5. The desktop creates a **new attempt jar** and permits exactly **one** automatic retry for the logical attempt, including subsequent subtitle/metadata stages. It never overwrites an active jar.

The rendezvous uses `TimeProvider.System` in production. Regression scenarios inject a controlled clock so the 600 ms expiry case is advanced explicitly, rather than accidentally measuring DPAPI/disk/thread-pool speed on a shared CI worker. An ACK received at the expired deadline cannot establish refresh success. Production wait/heartbeat limits and retry counts are unchanged.

Browser/host unavailable, permission revoked, disabled/missing profile, timeout, rejection and a second auth failure retain the prior snapshot, stop automatic auth retries and show localized manual Sync/Retry guidance.

Upcoming LIVE probes use the same bounded runner. Active start/reconnect defers refresh to the **whole part-attempt boundary**: the existing executor first finalizes/preserves the failed part, then the fresh jar starts the next numbered part. No retry writes into the failed capture or changes LIVE/VOD concurrency.

Regression coverage includes actual child-process auth failure, framed native watch requests, acknowledged newer snapshots, the failure matrix, unchanged leased jars and LIVE boundary ordering. No real account session is a test prerequisite.

## Expert arguments

Validation uses an embedded **option spelling/arity catalog**, not an executable copy of yt-dlp's parser. It was extracted from upstream `options.py` at `897341241f2bfb2439fe0367f4c7c9ccd29a3828` (292 option groups / 363 spellings). `scripts/Update-YtDlpOptionCatalog.py` regenerates facts for review; upstream code is not executed by the application.

Exact/unique abbreviated long options, inline values and short clusters are resolved case-sensitively before applying the central ownership restrictions. Cookies/login/netrc/certificates, credential headers, aliases/presets/config injection, executable/download/plugin/runtime ownership and credential traffic dumps are rejected with canonical reasons. `--extractor-args` is conservatively reserved because opaque arguments can introduce authentication/provider behavior; use a future reviewed typed profile instead. Safe rate/retry/output controls, ordinary headers, quoted values and harmless `--ignore-config` remain available. Unknown/ambiguous options and extra positional URLs fail closed; newly added safe options require a catalog review.
