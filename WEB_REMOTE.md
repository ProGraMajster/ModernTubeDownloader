# Local network remote (1.1.0 candidate)

The web remote runs **inside** ModernTubeDownloader and uses the same queue, worker, metadata analysis, settings, and history services as the desktop UI. It is disabled by default; no HTTP listener exists until it is enabled in Settings.

## Enable and connect

1. In **Settings → Local network remote**, choose an active private IPv4 address from the list, set a port (default `18765`), check **Enable web remote**, and save. `127.0.0.1` is for testing on the same PC only; choose a `10.x.x.x`, `172.16–31.x.x`, or `192.168.x.x` address for another device.
2. Read the address shown in Settings, or choose **Show QR code**. The QR contains only the HTTP/HTTPS address, never a code or credential.
3. Open the address on a device in the same trusted LAN. **Require pairing** is on by default: enter the short code shown in Settings. **Copy code** and **New code** help with pairing. New code preserves already paired sessions; logout, stopping/restarting the listener or changing authentication revokes them. Code generation is automatic on enable/save when no valid unused code exists, never on repaint. Settings distinguishes Available (mm:ss), Used (existing device keeps its session), Expired, attempt limit and Inactive. Codes and sessions are memory-only. Explicitly disabling pairing permits every reachable device to control the queue; the browser displays a warning.
4. If Windows Firewall asks about access, allow only private networks you trust. The app does not create firewall rules, enable UPnP, forward ports, or provide a cloud relay. If the page is unreachable, check the selected interface, device network, and firewall policy. The app never binds to `0.0.0.0` or a public IP.

Saving a disabled setting stops the listener without restarting the app. Closing the app stops it cleanly. The address/port is checked against active private IPv4 interfaces at startup; if an interface has changed, select its new IP and save again.

## Security boundary

HTTP is default and unencrypted: codes and cookies can be observed by parties able to inspect LAN traffic. Optional HTTPS is described below; pairing alone does not encrypt HTTP. Use only trusted private networks, not public Wi-Fi. Never port-forward this listener or expose it to the Internet. Binding alone cannot prevent forwarding by a router, VPN or firewall; the operator remains responsible for the network boundary.

An eight-character code (`23456789ABCDEFGHJKLMNPQRSTUVWXYZ`, displayed `ABCD-EFGH`) has 40 bits of entropy, ten-minute TTL and single-use semantics. Attempts are limited to five per minute per client and eight per code across clients; exhausted codes must be regenerated. `POST /api/pair` exchanges it for a fresh 256-bit opaque session, not a shortened bearer. The browser receives an HttpOnly, SameSite=Strict cookie, Secure on HTTPS, with an absolute eight-hour lifetime. Server-side lookup stores a hash. JavaScript cannot read this cookie; localStorage/sessionStorage contain no credentials. Codes and sessions never enter persistence, QR, URLs or logs. Legacy diagnostic bearer authentication remains supported, but is not exposed by browser UI.

Pairing and cookie-authenticated mutations require exact same Origin and `X-MTD-Request: 1`. Foreign Host/Origin headers are rejected and CORS is not enabled. Disabling pairing does not remove these checks, private-IP binding, body/rate limits or two-analysis concurrency. CSP, frame ban, no-cache and textContent rendering remain.

## Optional HTTPS and certificate trust

Save the selected IP first. Explicitly choose **Generate / regenerate certificate**, enable HTTPS and save. The one-year self-signed RSA certificate has the selected IP SAN and server-authentication usage. Changing IP requires regeneration. Missing, expired, corrupt or mismatched certificates give a localized error; HTTPS never silently downgrades. Return to HTTP only by explicitly disabling HTTPS.

Private PFX bytes are encrypted with per-user Windows DPAPI under `WebRemote/certificate.protected`; ACLs restrict the directory to the current user and LocalSystem. Decrypted PFX bytes exist only in memory. Windows TLS uses a transient per-user key container, not a persisted plaintext PFX. Export saves the public certificate only; Settings displays its SHA-256 fingerprint. No root trust or firewall rule is installed automatically.

Self-signed certificates are not automatically trusted by Chrome or phones. Verify the fingerprint and use your device's deliberate trust procedure if desired. Ignoring a warning is not certificate validation. Real Kestrel TLS tests verify the exact certificate/IP and Secure cookies, not phone trust UX. [Kestrel documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel?view=aspnetcore-10.0).

## Language

Default **🔄 Follow app / Z aplikacji** follows the desktop PL/EN language. Public `/api/auth-mode` returns only `authenticationRequired` and `desktopLanguage`; the unpaired login screen and authenticated dashboard both poll it every two seconds while visible. Changing desktop language updates labels/errors/footer without reload. Explicit **🇵🇱 Polski**, **🇬🇧 English** or **🌐 Browser language** overrides are localStorage preferences, not credentials. Browser mode uses the first preferred browser language (pl-* → PL, otherwise EN); legacy Auto preferences migrate to Browser, while fresh clients default to Follow app. Selecting Follow app resumes sync. Request-specific DTO localization does not change desktop language. Theme follows browser preference. Unchanged polling does not replace queue/LIVE nodes or repeatedly rewrite static labels; a changed language/data snapshot invalidates the relevant view.

The remote add form accepts only YouTube-family HTTPS URLs (`youtube.com`, its subdomains, `youtu.be`, and `youtube-nocookie.com`) in this MVP. This deliberate narrower scope reduces server-side request forgery risk from untrusted remote clients. The desktop application still supports other yt-dlp HTTP(S) sites. API clients cannot supply paths, shell commands, arbitrary yt-dlp/FFmpeg arguments, tool configuration, or raw logs. They cannot shut down the app.

The queue API returns presentation fields, not local file paths or raw metadata. Thumbnails, when present, are fetched through the existing thumbnail cache and returned as PNG after authentication; the browser does not receive the thumbnail source URL. A failed operation returns a bounded error code; technical details remain in local sanitized logs.

## Remote interface and API

The small responsive HTML/CSS/JS panel polls every two seconds while its tab is visible. It shows active/queued items normally and completed items in a collapsed section. Queue pacing countdown comes from the desktop scheduler's transient next-admission time. The panel also shows queue position, title, status, progress, speed/ETA, quality/container, selected time range, retry attempt, error key, and thumbnail when available. It can add a video or playlist, pause/resume the queue, cancel an active item, retry a failed item, remove a non-active item, and move a queued item. UI labels follow the browser-local PL/EN language; it follows the browser's light/dark preference.

The LIVE section has separate active, scheduled and partial/recoverable groups, elapsed duration, bytes/speed, part count and next-check/reconnect times; no percentage. `/api/live` observes the desktop `LiveSessionService`, not the ordinary queue. Stop and save, Cancel and Resume call that central service and its independent scheduler. Pausing the VOD queue has no effect on LIVE. Adding an active or upcoming URL through the ordinary add endpoint is deliberately rejected with **Open in desktop app**: starting a long recording requires desktop confirmation. Completed replays remain ordinary VOD downloads.

`GET /api/auth-mode` is public and exposes only whether pairing is required and the normalized desktop PL/EN language. `POST /api/pair` and `/api/logout` pair and revoke the current session. Endpoints below require a session (or legacy diagnostic bearer) when pairing is enabled:

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/status`, `/api/queue` | Queue state and summary |
| GET | `/api/queue/{id}/thumbnail` | Cached PNG thumbnail |
| POST | `/api/queue/add` | Analyze URL, validate quality/container/range, queue via existing services |
| POST | `/api/queue/{id}/cancel`, `/retry`, `/up`, `/down` | Bounded ordinary per-item actions |
| DELETE | `/api/queue/{id}` | Remove non-active item |
| POST | `/api/queue/pause`, `/resume` | Queue admission control |
| GET | `/api/live` | Separate safe LIVE session projection |
| POST | `/api/live/analyze` | Bounded classification/title/schedule projection, no raw metadata |
| POST | `/api/live/{id}/stop`, `/cancel`, `/resume` | Central LIVE Stop and save, Cancel, Resume |

The add payload uses `url`, `quality` (`best`, `2160`, `1440`, `1080`, `720`, `480`, `360`, `audio`), `container` (`Auto`, `Mp4`, `Mkv`, `WebM`), `rangeMode` (`full` or `custom`), optional `from`/`to` in `hh:mm:ss`, and optional `useSubtitleDefaults` / `useSponsorBlockDefaults` booleans. It has no raw command-line fields. The two switches can only request the app's saved policy; they cannot specify languages, categories, browsers, profiles, or cookie data. A custom range must have at least one bound; known media durations are enforced. A selected playlist uses one range for all entries; if any selected entry has a known duration shorter than the requested range, the whole add is rejected rather than silently truncated. Custom ranges cannot be combined with enabled subtitle/SponsorBlock defaults; SponsorBlock removal cannot be combined with subtitles.

User-selected Netscape `cookies.txt` authentication, when explicitly enabled in desktop Settings, is used server-side during analysis and download. The remote cannot query, select, or change the file path, and queue responses omit cookie configuration.

## Phone-friendly time range editor

Choose **Custom / Niestandardowy** and enter **Hours / Minutes / Seconds** separately for each bound. Each touch field uses a numeric keyboard, has a visible label and a 48 px minimum height; no colon needs to be typed. Blank components within a nonempty bound mean zero. Leave the entire From group empty to start at the beginning, or To empty to continue to the end. A preview shows the resulting range. Minutes/seconds must be 0–59, the end must be later than the start, and the existing 30-day limit remains enforced. Switching to Full media retains edits for later, disables the hidden fields and sends empty bounds.

The server contract is unchanged: the client assembles `hh:mm:ss` (or `d.hh:mm:ss` above 24 hours), not six new API properties. Tests: `node --test ModernTubeDownloader.Tests/WebRemoteTimeInput.test.cjs`; these 25 cases also run in CI. HTTP integration tests check embedded assets and retain the original API range test.

2026-10-04 physical phone feedback: the user reported working LAN connection/add flow and then a wrong keyboard for the former colon-delimited fields. After the change, the user confirmed the separate numeric fields are substantially better. Chrome checks at 320/390 px portrait, 844 px landscape and 1024 px desktop found no horizontal overflow and retained edits through Full/Custom. A real public Sintel 2–10 s request completed with History, VP9/AAC, 8.0078 s duration and a successful full short decode; the user confirmed playback. This does not validate every phone, token-ON pairing, QR, firewall prompt or the entire LIVE control matrix.

## Validation limits

Real loopback HTTP/HTTPS tests cover opt-in, pairing, single-use/expiry/attempt limits, cookie attributes, logout/restart/auth-mode revocation, Origin/Host checks, queue actions and shutdown. Certificate tests cover SAN, protected storage/ACL, public-only export and regeneration. These are not phone or multi-device LAN tests. Dated 1.0.0 feedback above is historical; candidate evidence and limitations are in [BROWSER_REMOTE_1_1_IMPLEMENTATION.md](BROWSER_REMOTE_1_1_IMPLEMENTATION.md).
