# System notifications — candidate.4

Optional Windows OS notifications, distinct from in-app status/toasts. The application consumes current ModernFormsNext `ISystemNotificationService`, `SystemNotification` and `SystemNotificationCapabilities`; it has no custom WinRT wrapper, COM activator, toast window or history store.

## Ownership and lifecycle

`Program` registers `WindowsSystemNotificationService` with **Automatic**, stable per-user app identity/display icon, on the UI thread before `Application.Run`. Official AppSdk/WinRT project references accompany the application. Known optional SDK failures are handled by MFN selection; an unexpected registration/icon failure is fully logged and disables only this feature.

`AppServices` attaches `SystemNotificationCoordinator` to existing queue/LIVE change events **before** starting their independent processors. Queue events capture immutable notification snapshots under the queue lock; LIVE events copy sessions. No second scheduler, timer loop or media worker. History is consulted for completed-file actions, not duplicated to generate a second completion notification.

Submission is serialized off download execution. State transitions and queue rounds are deduplicated. Master OFF performs no Show/Update/status-query/image work in coordinator and Settings' refresh, and does not queue old events for replay. Shutdown first unsubscribes, drains and asynchronously disposes the official provider while MFN's dispatcher still runs, then stops application services. Native errors do not change media state or block the download.

## Settings and defaults

Settings → **Powiadomienia systemowe / System notifications**. Values are persisted as `AppSettings.Notifications`; migration from old settings fills defaults, unknown enums normalize. Reset affects only these preferences. Basic status is Ready/Disabled/Unavailable; backend/Windows status is behind Advanced. Send test honors sound, privacy and supported presentation, without requiring any media.

| Preference / event | Default | Trigger |
|---|---|---|
| Enabled | ON | Global switch |
| Privacy mode | OFF | Generic text; no title/channel/URL/playlist/image |
| Cached thumbnail | ON | Valid existing local cache, at most 3 MiB; never another HTTP fetch |
| Sound | Default | Windows default or Silent |
| Presentation | Normal | Center only when native SuppressPopup capability exists |
| Dismiss on cancel | OFF | This logical key only, when Removal is supported |
| Download started | OFF | First active state, not every video/audio/merge transition |
| Download completed / failed | ON / ON | Terminal transition |
| Download retry / cancelled | OFF / OFF | New scheduled retry / terminal cancellation |
| Queue completed | ON | Round has no queued, waiting, retrying or active jobs; final completed/failed count |
| Download progress | OFF | One initial Show; updates at most once/second on existing events |
| LIVE waiting | ON | Transition to WaitingForLive; polling/countdown do not notify |
| LIVE transmission started | ON | WaitingForLive → Pending |
| LIVE recording started | ON | Initial Recording |
| LIVE connection lost / restored | ON / ON | Reconnecting / next Recording, including intermediate Starting |
| LIVE part saved | OFF | New retained part while active |
| LIVE completed / partial / failed | ON | Distinct terminal transitions |
| LIVE cancelled | OFF | Terminal cancellation |
| Critical application errors | ON | Existing unobserved-task error boundary; not fabricated tool update events |

No tool-version polling or invented ToolManager events were added. All visible labels/messages/actions use shared PL/EN catalogs; existing localization parity checks cover the new keys.

## Identity, progress and privacy

Media images reuse only completed entries in the existing persistent thumbnail cache. Skia decodes locally (no notification HTTP), with a 3 MiB input/output limit and 4-million-pixel decode limit; a proportional, at-most-512px PNG representation is atomically retained as `<cache-key>.notification.png`. JPEG/PNG/WebP all use this representation, including extensionless-format `.img` entries. Both source and derived files survive disposal. Missing, invalid or oversized cache entries remain text-only.

The common Thumbnail role maps to Windows Hero, not a small text-side picture. Candidate.4 uses typed Windows AppLogo placement (no circle crop), falling back to Inline when advertised, then text-only. This presentation choice is separate from the confirmed WebP validation defect: Windows rejects WebP with InvalidImage, whereas the old JPEG `.img`/Hero was visibly valid. Privacy and thumbnails OFF skip cache lookup altogether. Diagnostics record hashed cache key, local path/type/size, selection/capabilities, before/after existence and every provider warning without logging the thumbnail URL. Accepted is still not visual proof; see the candidate.4 report for owner-confirmed native evidence.

Downloads use group `downloads`, key `download-<QueueItemId:N>`; LIVE uses `live`, key `live-<LiveSessionId:N>`. Terminal content replaces the same logical identity on rich replacement-capable providers. Downloads progress requires Progress + LiveUpdates + Replacement; a pending initial native submission is reserved so slow backends cannot create repeated Shows. Percentage is clamped/finite; speed and ETA use the current snapshot. Completion removes progress and offers appropriate actions.

LIVE never fabricates a percentage. If actual Progress + IndeterminateProgress are supported, it uses null Value and duration/bytes text; byte-state events update at most once/second when LiveUpdates is supported. Otherwise it submits ordinary text. Retry/poll events do not each generate banners. Language changes update only already-shown progress with the required capabilities and the same event throttle; they do not create progress banners on a limited provider. Removing a LIVE session clears its local progress identity bookkeeping.

Privacy is checked again at submission/update time. Payloads carry IDs/actions only, not media metadata. Existing OS history from before enabling privacy is **not retroactively erased**; privacy applies to future submissions. Windows may show notifications on a lock screen under its own policy. Turn notifications off or adjust Windows lock-screen policy for stricter protection. The application never clears all notification history at startup.

Image lookup reuses `ThumbnailCacheService`'s URL-hash file; missing/oversized/invalid image headers produce text-only content. No notification-specific downloader/cache. Error urgency is high only if supported; completion/reconnect remain normal. Windows sound/suppress popup options are typed official platform options.

## Safe actions and activation

Completed Downloads: Open file / Open folder. Failed: Retry / Open app. Actual LIVE Recording: Open LIVE / Stop and save. Other LIVE states: Open LIVE only. Capability action limits are respected.

Arguments are a bounded versioned intent: group, GUID, view, allowlisted action. No shell command, raw path or URL. Native logical key/group/action must match. `NotificationActionPolicy` rechecks **current trusted** queue/history/LIVE state: Retry only Failed; Stop/save only Recording; file/folder only an existing trusted output file. A deleted item/file or stale state is navigation-only.

Body routes to Downloads/History/LIVE. Downloads tries to select/reveal its current queue card. Missing IDs simply open the requested view. The existing `ExternalLinkInstance` current-user named pipe is also the notification transport; no second single-instance mechanism. A secondary official COM receiver runs a closeable MFN dispatcher solely to validate/forward activation and dispose the provider; it never creates AppServices, tools, Form or schedulers. A true cold start is the primary process, with provider startup activation dispatched after UI/IPC binding. App SDK's exact native startup marker is accepted, not arbitrary command-line arguments. Classic/Shell do not gain synthetic cold activation.

## Windows deployment

- MFN pin: `69c5ea2b20b07d9df801340c8ae9e3e1ca5dfbc3`, freshly fetched master 2026-10-10.
- Consumer TFM: `net10.0-windows10.0.26100.0`; minimum declared platform 17763. Supported operating systems must also satisfy actual .NET 10 requirements; old Windows versions are not certified by the TFM.
- `WindowsSdkPackageVersion=10.0.28000.87` is explicit in central props **before first restore**, avoiding first-restore projection mismatch/NETSDK1112.
- Both providers are solution projects, including Visual Studio restore graph. Code-first MFN disables App SDK's default Content item glob to avoid making embedded AppIcon.png an unintended output Content item.
- Distribution remains self-contained .NET win-x64, multi-file, untrimmed, no ReadyToRun/NativeAOT/single-file. It is **not** self-contained Windows App Runtime. App SDK runtime self-contained deployment and eager module initializers are disabled; no runtime installer is bundled/auto-run.
- Automatic tries officially supported App SDK, then Classic WinRT, then Shell (when allowed), else Unavailable. Rich images/actions/progress/history/cold activation differ per provider. Elevation, OS policy, focus assist and installation state affect availability/display.
- Windows App SDK packages use their Microsoft software terms, not MTD's MIT license. Exact package licenses/notices and Windows projection metadata/CsWinRT notice are packaged; see [Third-party notices](THIRD_PARTY_NOTICES.md).

## Validation and limits

Deterministic coordinator/action tests use a fake **OS service**, not fake evidence of a visible Windows banner. They cover OFF/no work, progress/slow submission, final round counts, failure/retry, LIVE dedup/reconnect/partial/completion, PL/EN/privacy/images, disabled/denied/unavailable, optional exceptions, trusted/deleted action state and existing IPC.

Real app native Settings smoke invokes the test button through MFN's canonical automation tree; its log records the actual selected provider/submission status. Accepted means native acceptance, **not** a visible banner or working cold action. Direct tool-host launch initially produced Accepted/WPN delivery events but no user-visible notification. The same isolated harness launched through Explorer produced a visible notification; the owner confirmed that clicking opened Downloads in the same running instance. The precise host-context cause is not established. No notification policy, service or permission was changed and no registry was manually edited; normal official provider identity registration remains provider-owned.

The earlier scoped PASS for test notification visibility and warm Open app routing is retained in [candidate.3](CANDIDATE3_NOTIFICATIONS_PIPELINE_REPORT.md). Candidate.4 additionally has owner-confirmed real cached media images for completed/progress/LIVE and image absence for privacy/OFF; progress/LIVE screenshots are local ignored QA artifacts. Event states are controlled presentation fixtures, not evidence of a full replay download or LIVE recording. Exact current results and acceptance recommendation: [candidate.4 report](CANDIDATE4_THUMBNAIL_LIVE_REPLAY_REPORT.md). Cold activation, file/folder/retry/LIVE actions, untested Windows versions and clean PCs are not reported as passes.
