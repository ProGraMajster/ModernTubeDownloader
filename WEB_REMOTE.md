# Local network remote (opt-in MVP)

The web remote runs **inside** ModernTubeDownloader and uses the same queue, worker, metadata analysis, settings, and history services as the desktop UI. It is disabled by default; no HTTP listener exists until it is enabled in Settings.

## Enable and connect

1. In **Settings → Local network remote**, choose an active private IPv4 address from the list, set a port (default `18765`), check **Enable web remote**, and save. `127.0.0.1` is for testing on the same PC only; choose a `10.x.x.x`, `172.16–31.x.x`, or `192.168.x.x` address for another device.
2. Read the address shown in Settings, or choose **Show QR code**. The QR contains **only the HTTP address**, never the token.
3. On a device connected to the same trusted LAN, open that address. Authentication is **on by default**: enter the token shown in Settings. **Copy token** and **Show token** help with pairing. **New token** immediately invalidates previous browser sessions. The token is random and kept only in process memory; a restart creates a new one. If you explicitly turn off **Require access token** in Settings and save, the panel opens without a token; every device able to reach this LAN address can then control the queue.
4. If Windows Firewall asks about access, allow only private networks you trust. The app does not create firewall rules, enable UPnP, forward ports, or provide a cloud relay. If the page is unreachable, check the selected interface, device network, and firewall policy. The app never binds to `0.0.0.0` or a public IP.

Saving a disabled setting stops the listener without restarting the app. Closing the app stops it cleanly. The address/port is checked against active private IPv4 interfaces at startup; if an interface has changed, select its new IP and save again.

## Security boundary

This is HTTP, **not HTTPS**: the address and token can be observed by other parties able to inspect traffic on the LAN. Use it only on a trusted private network, not public Wi-Fi or a network with untrusted devices. Do not configure router port-forwarding or expose the port to the Internet. The listener is bound to one explicitly selected IP, but binding alone cannot prevent a router, VPN, or local firewall policy from forwarding traffic; the operator remains responsible for the network boundary.

With authentication enabled, every queue API request requires `Authorization: Bearer <token>`; `/api/auth-mode` exposes only whether authentication is required. The token is never put into the URL, QR, normal logs, or server responses. The browser keeps it in tab-scoped `sessionStorage`, not a cookie. Turning authentication off removes the token check, **not** the private IPv4/loopback binding, Host/Origin checks, request size limits, per-IP rate limits, or two-analysis concurrency limit. Foreign `Origin` and `Host` headers are rejected; CORS is not enabled. A content-security policy, frame ban, no-cache policy, and `textContent` rendering reduce injection risks. This does not turn HTTP into an encrypted transport.

The remote add form accepts only YouTube-family HTTPS URLs (`youtube.com`, its subdomains, `youtu.be`, and `youtube-nocookie.com`) in this MVP. This deliberate narrower scope reduces server-side request forgery risk from untrusted remote clients. The desktop application still supports other yt-dlp HTTP(S) sites. API clients cannot supply paths, shell commands, arbitrary yt-dlp/FFmpeg arguments, tool configuration, or raw logs. They cannot shut down the app.

The queue API returns presentation fields, not local file paths or raw metadata. Thumbnails, when present, are fetched through the existing thumbnail cache and returned as PNG after authentication; the browser does not receive the thumbnail source URL. A failed operation returns a bounded error code; technical details remain in local sanitized logs.

## Remote interface and API

The small responsive HTML/CSS/JS panel polls every two seconds while its tab is visible. It shows active/queued items normally and completed items in a collapsed section. Queue pacing countdown comes from the desktop scheduler's transient next-admission time. The panel also shows queue position, title, status, progress, speed/ETA, quality/container, selected time range, retry attempt, error key, and thumbnail when available. It can add a video or playlist, pause/resume the queue, cancel an active item, retry a failed item, remove a non-active item, and move a queued item. UI labels follow the desktop language (PL/EN); it follows the browser's light/dark preference.

The LIVE section has separate active, scheduled and partial/recoverable groups, elapsed duration, bytes/speed, part count and next-check/reconnect times; no percentage. `/api/live` observes the desktop `LiveSessionService`, not the ordinary queue. Stop and save, Cancel and Resume call that central service and its independent scheduler. Pausing the VOD queue has no effect on LIVE. Adding an active or upcoming URL through the ordinary add endpoint is deliberately rejected with **Open in desktop app**: starting a long recording requires desktop confirmation. Completed replays remain ordinary VOD downloads.

`GET /api/auth-mode` is available without a token so the page can choose its login state. The following endpoints require a token only when authentication is enabled:

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

The automated HTTP test uses a real loopback Kestrel listener and covers opt-in, authentication, token rotation, Origin/Host checks, input validation, add, pause/resume, retry/remove/cancel, mutation rate limiting, enable/disable, and shutdown. It is **not** a phone or multi-device LAN test. Firewall prompts, different router/AP configurations, mobile-browser rendering, and actual untrusted-network resistance require separate manual validation before release.
