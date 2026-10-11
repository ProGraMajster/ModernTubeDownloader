# ModernTubeDownloader Browser Extension (1.1.0)

Unpacked Manifest V3 extension for Chrome/Edge. Toolbar and page/link menus hand off ordinary HTTP/HTTPS URLs to the desktop. yt-dlp determines actual support; accepting a URL is not a promise it can be downloaded. In-page buttons are adapter-based enhancements, currently YouTube only. No browser-store release is published.

## Install and use

1. Build/publish the app and launch it once. Normal startup repairs the current-user `moderntubedownloader://` association without admin rights. After moving the app, launch it from its new location. Desktop Settings can disable/repair browser integration independently of Web Remote.
2. In `chrome://extensions` / `edge://extensions`, enable Developer mode and load this `browser-extension` folder unpacked. After updating scripts/manifest, reload the extension and existing pages. `MTDContext` detects an invalidated old realm and removes its own button/modal, observers, timers and listeners. The next normal page reload loads a fresh script; immediate reinjection is deliberately not requested with wider permissions.
3. On HTTP/HTTPS pages use the toolbar popup: **Analyze in app** is the primary action. **Add to queue** is explicit QuickQueue; generic sources use Best/Auto. Internal browser URLs, local files, URL credentials and executable/raw-argument payloads are rejected.
4. Context menus default to Analyze. Popup settings independently disable injected buttons/context menus and change the context-menu default to QuickQueue. Preferences are browser-local.

### Updating already-open YouTube tabs

When updating from a version without the context lifecycle guard, **reload each already-open YouTube tab once (F5)** after reloading the extension. The extension reload cannot retrofit cleanup into an older script still attached to that tab. YouTube SPA navigation (including the next video in a Mix) does not replace that script. A stale button can therefore still raise `Extension context invalidated` when clicked, even though fresh tabs use the corrected code. The guarded version removes its own stale UI on subsequent extension reloads; reload the page to restore the button. There is no automatic page-reload loop.

One `extensionLanguage` preference controls popup, Options, menus, injected button and an already open modal. **🌐 Automatic / browser** is default (pl-* → PL, otherwise EN); **🇵🇱 Polski** and **🇬🇧 English** override it. The popup top-right language button applies changes immediately and offers **⚙ Language settings**; **⚙ Settings** uses `chrome.runtime.openOptionsPage()`. The real Manifest V3 Options page contains language, YouTube injection, context menus, default action, last YouTube quality, version/about and Restore defaults with a confirmation dialog. Reset removes only these owned preference keys, not unrelated Chrome storage. Options follows browser light/dark preference and works at narrow widths. Labels accompany flags for accessibility.

Chrome getMessage cannot choose a locale: Auto and manual overrides load the same existing packaged PL/EN catalogs and retain a local snapshot, not a duplicate translation system. After invalidation translation never calls Chrome i18n/storage/runtime. Only recognized invalidation triggers cleanup; unrelated storage/network errors remain reported. Popup/Options/background subscriptions and serialized context-menu reconstruction use the same guard. Storage changes update live surfaces and rebuild context menus without restarting Chrome. Manifest/store metadata still uses the browser-selected Chrome locale. Desktop language-follow sync remains absent; candidate.5 Native Messaging is specifically for opt-in cookies, not a general return channel. The generic popup retains its scoped `[hidden]` CSS rule so YouTube-only quality controls stay hidden.

## YouTube adapter

Watch, Shorts, `/live/<id>` and playlists retain compact insertion, per-context styles and SPA reconciliation. Clicking the page button only opens a centered modal; queueing requires explicit confirmation. Multiple action anchors and a compact fallback avoid reliance on one brittle class. Missing an anchor? Use toolbar/menu.

Modals share last-quality preference. Presets mean “up to”; desktop analysis chooses available compatible formats. **Analyze in app** opens canonical preview for format/range/subtitle/SponsorBlock choices. Explicit playlist QuickQueue retains add-all; default Analyze uses per-entry selection. Watch URLs with a playlist parameter remain one video.

`/live/` is a URL shape, not proof of active broadcasting. Its modal and popup offer both Analyze and explicit Add to queue. QuickQueue is source-neutral and desktop metadata analysis runs first: completed replay/was_live enters regular VOD; active/upcoming opens existing LIVE confirmation, never automatic capture. The player timestamp `t` remains part of the URL, not a requested download range. Quality is a ceiling validated against actual formats. No parallel scheduler/extractor catalog is introduced.

No other adapter is claimed: generic handoff does not need DOM integration for every extractor. Desktop analysis reports private/authentication/unsupported errors. Ordinary integration never reads cookies; optional candidate.5 cookie access requires separate explicit domain consent. Private player/session data is not scraped.

## Transport and security

- Bounded v2 protocol: `moderntubedownloader://open?v=2&action=analyze|quick-queue|open&url=...&quality=...&container=auto&playlist=0|1&source=browser-extension&id=...`. Decode once; unknown fields, mixed legacy flags, unsafe URLs and raw arguments are rejected. v1 stays YouTube-only for compatibility. Generic QuickQueue is normalized to Best/Auto by desktop.
- Existing current-user single-instance named pipe forwards requests. No special manually launched process or localhost service is needed; handoff is independent of Debug UI Automation.
- Required permissions remain `activeTab`, `storage`, `contextMenus`; scripts remain YouTube-scoped. Optional `cookies`, `nativeMessaging`, `alarms` and optional HTTPS wildcard support custom domains, but grants request only explicit chosen origins. No history/download access.
- Custom media protocol still has no queue-acceptance response: it cannot enumerate real formats or bypass browser confirmation. Native cookie ACK confirms snapshot storage/deletion only, not media queue success.

## Opt-in cookies

Register the standalone host in desktop Settings using the exact public ID shown in Options and the chosen browser. Launch the desktop normally from Explorer; a package-private redirected AppData store is rejected. Consent UI shows every selected domain and credential notice before requesting optional access. Current service, selected services/domains and Custom cookie identities are supported; Custom shows metadata, never values. YouTube scope is only youtube.com, not Google account domains.

Before use/change/startup default ON after consent; periodic OFF, configurable 5–1440 min. Worker uses events and coalesced alarms, not permanent polling. Handoff awaits native ACK; failure asks before continuing. Disabling sends a newer tombstone and removes unused domain permissions. Pending deletes preserve native retry authority; a newly regranted API may require one extension reload, as observed in real Chrome. No automated reload loop is added. Values never enter Chrome storage, DOM or media URI. Partitioned records are excluded by the host and counted.

With before-use consent, a metadata-only Native Messaging watch port also accepts desktop refresh requests after yt-dlp auth failure. The worker rechecks consent/permissions and sends a new correlated snapshot; only an accepted greater version allows one desktop retry. Closing/revoking the last eligible profile closes the port. Credentials still use only the existing snapshot transport, never Web Remote/URI/Chrome storage. See [COOKIE_SYNC.md](../COOKIE_SYNC.md) for the bounded failure behavior, jar lifecycle and authority limits.

Chrome selected-domain qualification uses synthetic example.com only; authenticated YouTube and Edge/Brave/Chromium are not qualified. The [candidate.5 report](../CANDIDATE5_COOKIE_SYNC_YTDLP_AUDIT_REPORT.md) is historical baseline evidence, not the final reverse-refresh implementation.

## Development checks

`node --test browser-extension/tests/*.test.cjs` covers validation, i18n, preferences, focus and YouTube reconciliation. Run .NET and native Automation separately. Real Chrome/protocol prompts/device trust cannot be inferred from fixtures. Scoped evidence: [BROWSER_REMOTE_1_1_IMPLEMENTATION.md](../BROWSER_REMOTE_1_1_IMPLEMENTATION.md).

Existing icon PNGs derive from `ModernTubeDownloader/Assets/AppIcon.png`; branding is unchanged.
