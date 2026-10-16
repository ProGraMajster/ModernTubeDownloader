# Optional Modern Dashboard interface

Development version: **1.3.0-candidate.1**, unreleased. Classic remains the default
for existing and new profiles. This branch does not publish or replace 1.2.0.

## Selecting an interface

Open Settings → Personalization → Interface style. Choose **Classic** or
**Modern dashboard** (PL: **Klasyczny** / **Nowoczesny panel**). Native miniature
previews illustrate both layouts. The selection applies immediately and is saved
in `appearance.json`; it does not modify download, authentication or update settings.

The Modern Dashboard preset selects the dashboard, Dark theme and 10-unit corners.
Selecting the interface alone preserves the owner's other appearance preferences.
Default/Compact/Large/Minimal presets retain their Classic behavior.

## One runtime and persistent pages

`AppServices.Create` remains the single composition root. `MainForm` owns the same
Downloads, LIVE, History, Other, Batch and Settings pages for its whole lifetime.
`AppShellHost` moves their common content panel between `ClassicAppShell` and
`ModernDashboardAppShell`. It constructs the next chrome before detaching the old
one, detaches the shared pages, and disposes the previous chrome. Current page and
queue selection survive switching. Native titlebar buttons remain owned by MFN.

No shell constructs a queue, processor, LIVE scheduler, tray, notification service,
history store, Batch service, updater, cookie bridge, Web Remote or IPC listener.
There is no WebView, copied framework control or second accessibility tree.
`AppPageId` and `AppNavigation` express the common route targets. Tray, protocol,
extension and notification handlers remain in the existing MainForm boundary.

The Classic Batch modal hosts the same `BatchImportView` used by the dashboard
page. Its analysis and persistence outlive closing the presentation as before.
All writes to queued options use the existing `DownloadQueueService.Update` lock
and compatibility/format selectors. An item that becomes active during editing
rejects the edit. Output folder remains the canonical global setting because the
current queue contract has no independent per-item output-folder setting.

## Reference layout

The reference is a desktop layout with a 212-unit expanded sidebar, approximately
48-unit titlebar, URL/paste/split-action row, source actions, bordered queue surface,
compact thumbnail rows, a roughly 300-unit right inspector, three lower activity
widgets and a 32-unit status bar. Existing MTD branding replaces the reference logo.
The Dark palette uses graphite/navy semantic surfaces; the primary action blue is
slightly darker than the reference to retain 4.5:1 white-text contrast in every state.

Downloads supports quick queue, explicit analysis/options, the existing playlist
selection workflow, supported-source discovery, canonical pause/resume, completed
cleanup, item actions, keyboard selection and a details/options inspector. LIVE
keeps its independent recording ownership, active/scheduled/completed sections,
Stop and Save, reconnect/part facts and inspector. History provides search, filters,
paging, export, file/folder actions and canonical re-analysis/retry. Other lists
only working tools; the reference's unimplemented conversion/subtitle/thumbnail
utilities are intentionally absent. Settings retains all eleven categories,
adds category search and horizontal category chips, and flows its General overview
into three columns where space permits (other categories use up to two). Help/About expose branding,
dynamic version, pinned framework metadata, repository/license and diagnostics.

## Responsive layout and text

All bounds are logical MFN units. `AppTypography` retains absolute 80–200% text
scaling, separate from OS DPI. Dashboard presentation roots use a larger base
text recipe; Classic recipes are restored when returning to Classic. No
RenderScaling, physical-pixel conversion or presentation transform changes DPI.

The sidebar supports Expanded/Compact/Auto and native scrolling at insufficient
heights. Compact buttons retain names and tooltips. The inspector is inline only
when sufficient queue width remains; Details opens a section below the list at
smaller sizes. Always shows that section automatically, Hidden suppresses it.
Widgets stack at narrow widths or large text scales. Analysis actions stack at
large scales; page scrolling retains access to the resulting vertical content.
Long row titles use ellipsis, tooltips and bounded row heights. Fitting dashboard
labels paint through MFN's public Unicode DrawText API over the available field,
avoiding the premature tight-measurement ellipsis of the pinned Label renderer.
Classic delegates to the original renderer and restores its font/style recipes.

The reference rebuild uses subtle gradient cards, unframed navigation, native
vector action icons, thumbnail duration badges, separated row metadata, aligned
history columns and grouped inspector fields. Completed jobs do not display an
active blue progress bar. History date ranges (all/7/30 days), timestamp sorting,
audio/video/playlist filters and export all read the canonical history snapshot;
they do not modify its records. Preview elements in Settings are labelled.

Downloads and History render at most 40 rows per page. Revealing a notification
target changes to its page before selecting it. Batch retains its existing bounded
service page. Individual queue progress updates update the affected row and the
selected inspector, rather than reconstructing the full queue.

## Personalization and themes

Schema 2 adds `InterfaceMode` and `ModernDashboardShell` to `AppearanceProfile`.
Schema 1 migrates with Classic and safe dashboard defaults; unsupported versions,
unknown fields and invalid enum values remain rejected. Import/export/reset use
the same closed schema. Resetting the dashboard section preserves typography and
the interface choice; resetting the whole profile restores Classic.

Dashboard preferences include sidebar, inspector, widgets (All/Compact/Hidden),
queue density, thumbnail size, author/source/quality/format/size/speed/ETA metadata,
always/hover actions, speed chart and process visibility. Classic per-page options
remain independent. Dark, Light, System, Custom and accent overrides use the
existing appearance service and semantic tokens. The Light preset has its own
light surfaces, readable dark text and matching action colors.

## Activity, privacy and lifetime

One presentation timer per visible widget group updates at approximately 1 Hz.
The speed graph retains at most 120 samples. Disk checks run off the UI thread,
are throttled to 15 seconds, and use the selected output volume or configured
output folder. Canonical storage warnings take precedence over advisory disk data.
Unknown sizes and rates are not invented. Idle graphs do not generate artificial
activity. Process telemetry is scoped to children started by the existing runner;
it exposes allowlisted tool names and elapsed time, never arguments or credentials.

Thumbnails use the existing shared cache with cancellation and stale-result guards.
Pages keep one set of service subscriptions; chrome has no service subscriptions.
Disposed controls release theme subscriptions and typography layout leases.
Queue selection extends MFN's existing AccessibleObject provider and exposes
selection through the same semantic tree. Reduced-motion settings govern existing
page/progress/interaction animations.

## Recovery

Run `ModernTubeDownloader.exe --classic-ui` to force Classic for this session.
The saved Modern preference is retained. The argument can accompany `--automation`
in Debug or existing startup arguments. A dashboard construction/presentation
failure logs diagnostics, recovers Classic without restarting services and shows a
notice after the window is ready. No automatic rewrite creates a restart loop.

## Qualification

See `AUTOMATION_TESTING.md` for executable smoke commands. Automated layout tests,
native MFN Automation, production UIA, real source downloads and physical DPI are
different evidence categories. A local ZIP is not a published release. Owner visual
acceptance and unavailable physical DPI checks must remain explicitly reported.

### Framework findings

[MFN #272](https://github.com/ProGraMajster/ModernFormsNext/issues/272) tracks native
premature `Label.AutoEllipsis` for fitting text. Small app presentation subclasses
use public painting APIs while retaining the inherited Label, ProgressBar and
CheckBox providers and behaviors. No renderer implementation is copied and no
framework source is modified. Explicit transparent zero borders avoid Skia's
hairline behavior. Existing composite typography #204/#205 remains upstream.
See [reproduction and boundaries](MFN_DASHBOARD_REPRO.md). Physical DPI and owner
visual acceptance remain separate from native smoke and headless raster tests.
