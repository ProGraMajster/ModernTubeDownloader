# ModernTubeDownloader Browser Extension (local MVP)

This unpacked Manifest V3 extension adds a compact YouTube action, a quality-selecting popup, and a context-menu fallback for Chrome and Edge. It sends the current supported YouTube URL, requested quality preset, and explicit quick-queue or open-in-app intent to the desktop app. It does not read cookies, history, or downloads. It is not published to the Chrome Web Store.

## Install locally

1. Build/publish the Windows app, extract it, and launch `ModernTubeDownloader.exe` once. Normal startup creates or repairs the per-user `moderntubedownloader://` association silently; no terminal or administrator rights are needed. Moving the app requires one normal launch from its new location so the path can be repaired. Browser integration can be disabled or repaired in Settings.
2. In Chrome visit `chrome://extensions`, or in Edge visit `edge://extensions`. Enable Developer mode, choose **Load unpacked**, and select this `browser-extension` folder.
3. Open a supported YouTube video, Short, `/live/<id>` page, or `/playlist?list=...` page. The page button opens a centered dialog. Ordinary video/Short/playlist media offer quality selection and quick queueing or **Otwórz w aplikacji**. A `/live/` route always opens analysis in the app so a potentially long recording cannot start without explicit desktop confirmation. The browser may ask whether to allow opening the application.

The dialog and popup share the last selected quality, stored locally in the browser. Quality presets use “up to” semantics: if the exact height is unavailable, ModernTubeDownloader chooses the closest compatible lower format. Opening the page dialog never queues by itself. **Dodaj do kolejki** sends the quick request using the desktop app's saved subtitle and SponsorBlock defaults; **Otwórz w aplikacji** analyzes the URL and leaves queue confirmation and **More options** (subtitles/SponsorBlock) to the application. The context menu remains an explicit quick-queue action. An explicit `/playlist?list=...` URL queues every available entry; a `watch?v=...&list=...` URL remains one video. Playlist open-in-app uses the existing per-entry preview and workers. The extension never reads or sends browser cookies; optional user-selected Netscape cookies.txt authentication is configured only in desktop Settings. It does not read a browser database or bypass DPAPI.

For a **time range**, choose **Otwórz w aplikacji / Open in app** and use the desktop **Zakres materiału / Media range** controls after analysis. The quick extension dialog stays intentionally simple and does not auto-queue when opened; no extra range fields or transport parameters were added to the extension.

## Architecture and security

- The extension constructs a bounded, versioned `moderntubedownloader://open?v=1&url=...&quality=...&container=auto&analyze=1&queue=0|1&open=0|1&playlist=0|1&source=browser-extension&id=...` request. The app independently validates every field and the YouTube URL; only known action, quality, and container IDs are accepted. URL decoding occurs once and the payload is never passed to a shell.
- The app is single-instance. A second protocol-launched process forwards a bounded message to the first through a Windows named pipe restricted to the current user, then exits. This is independent of the Debug-only UI automation bridge. Only recognized YouTube watch, youtu.be, Shorts, `/live/<id>`, and playlist URLs are accepted.
- Ordinary desktop startup creates the HKCU association only when it is missing or stale. Settings can disable it (removing the app-owned association) or repair it. A future installer can take over registration without changing the request contract.
- The custom URI has no return channel. Therefore the extension cannot ask yt-dlp for the real format ladder, confirm queue insertion, or distinguish a missing handler from a browser prompt. It intentionally shows documented “up to” presets and does not scrape YouTube's DOM/player response. **Open in app** is the accurate path: yt-dlp determines available qualities there. Native Messaging could add request/response capabilities later, but requires a separately registered, authenticated host and a stable extension ID.
- The content button uses several YouTube action-area anchors and reconciles it after SPA navigation without duplicating it. If a page has no matching action area, use the popup or context menu.

`/live/<id>` is a URL shape, not proof that a broadcast is currently live. The extension does not infer `live_status` from YouTube's changing DOM. It opens that URL on the desktop **LIVE** page for yt-dlp analysis: replays offer a transition to Downloads, active broadcasts require explicit FromNow/experimental FromStart confirmation, and upcoming broadcasts enter the separate scheduled LIVE list. Watch/Shorts requests classified as live by the app are likewise redirected instead of silently recorded. LIVE has its own scheduler, concurrency limit and `live-sessions.json`, not ordinary VOD queue slots. Browser quick queueing is intentionally disabled for `/live/`; the existing transport is unchanged.

The extension does not infer playlist privacy from a single label, translated page text, or internal YouTube player data. There is no verified stable privacy attribute across page variants in this local MVP. A playlist may be visible to its signed-in owner while inaccessible to the separate yt-dlp session; the app reports the extractor's private/authentication error. A browser-side warning can be added only after a reliable, locale-independent signal is validated.

## Development checks

Run `node --test browser-extension/tests/*.test.cjs` from the repository root. Build and test the .NET solution separately. Before calling this release-ready, perform a real Chrome smoke with the app closed/open, popup 720p, Shorts, playlist, browser restart, and moved-EXE repair. Automated JavaScript tests cannot prove OS protocol-association behavior.

Icon PNGs in `assets/` are resized derivatives of the existing `ModernTubeDownloader/Assets/AppIcon.png` branding.
