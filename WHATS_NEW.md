# Versioned notes for the installed application

`WhatsNewForm` displays the reviewed embedded `Resources/ReleaseNotes.json` immediately,
then uses `GitHubReleaseNotesService` asynchronously to obtain the official GitHub Release
for that exact installed version. Opening the window never waits for network discovery.
The existing application updater remains the sole owner of available-version discovery,
verified packages and installation; this reader cannot install anything.

## Local catalog and version identity

Schema version 1 contains exact product versions, including prerelease suffixes. Every
language record has four distinct sections: `new`, `improved`, `fixed`, `limitations`.
The loader requires English, bounds the embedded resource to 256 KiB/20 releases and each
section to 12 nonempty items. Its content is reviewed source embedded in the application.

Stable `1.3.0` has complete local English and Polish notes. The previous
`1.3.0-candidate.1` record remains archival so exact-version lookup and candidate-to-stable
migration keep their existing meaning. Stable 1.3.0 never borrows that candidate's text.
There is no invented archive UI or selector for versions absent from the catalog.

Version comes from `UserExperienceService`/`AppBranding.Version`, not a user-provided URL.
The bounded ASCII version grammar excludes path separators, query strings and fragments.
Build metadata after `+` is removed from release identity; a prerelease suffix is retained.
Unknown versions do not substitute a previous, later or candidate record. Automatic notes
require an exact local record, and a missing record is not acknowledged as empty changes.

Other interface languages use the English bundled content with a visible fallback notice.
This is separate from the sixteen interface locale catalogs. Official online release text
is English and is explicitly identified as such in a non-English interface.

## Official source and validation

The only request target is:

`https://api.github.com/repos/ProGraMajster/ModernTubeDownloader/releases/tags/v{installed-version}`

The application uses its existing shared HTTP client and official repository constant.
It does not use `/releases/latest` for installed notes, send credentials, follow content
links or accept a different final response URI. A response must identify the exact tag,
not be a draft, have prerelease status consistent with the installed version, and have
the exact official release-page URL. Blank/missing bodies and invalid or oversized JSON
are rejected. A normal 404 before publication is a local/offline outcome, not a red error.

The reader bounds the API payload to 1 MiB and UTF-8 release body to 64 KiB. Canonical
release notes use four second-level headings: `New`, `Improvements`, `Fixes`, `Known Issues`.
Third-level headings, paragraphs and lists become plain text in those four existing cards.
Each card uses a retained read-only multiline editor with its own vertical scrolling
and a viewport at most 360 logical pixels high. All accepted text remains selectable;
long notes do not require full-document native bitmaps or clip at a fixed line count.
HTML, scripts, image markup and links are not active controls; no remote image is loaded.
The only external navigation action opens the constructed official page for this version.

Source labels distinguish `GitHub Release`, `Cache GitHub` and bundled local/offline notes.
An invalid response or failed request leaves validated cached content or the bundled record
available. It does not affect available-update state, profile migration or acknowledgement.

## Cache, cancellation and display lifetime

Cache lives in the profile's dedicated `release-notes-cache` directory, not in settings,
History or queue JSON. Envelopes retain the exact version, validated Markdown, official URL,
validation time and optional ETag. Reading cache revalidates its identity, size and body;
corrupt or redirected-path entries are ignored. Successful writes publish an exclusive
temporary file atomically; a cache-write failure does not make valid downloaded text unsafe.

Current limits are four retained tag files, at most 2 MiB per cache envelope, a six-hour freshness
period, an eight-second request deadline and at least thirty seconds between admitted
requests. Concurrent opens serialize through the same service. Refresh bypasses freshness,
not the request limiter. ETag enables `If-None-Match`/304 revalidation; 403/429 backs off
within bounded retry limits. Expired validated cache remains available when offline.

Closing cancels that window's work. Late responses are ignored after close, version change
or language mismatch. Responses update the retained four cards on the UI dispatcher;
they do not reconstruct the window or replace focused controls. The current scroll offset
is preserved within the new content bounds. No response can acknowledge a release.

## Display and acknowledgement

The native scrollable window measures content/actions against the actual viewport, theme
and logical text scale. Later, Got it, Appearance and Updates retain their stable automation
IDs; Refresh and the exact-release external action have explicit identities as well.

Opening notes does not mark them read. After the window has actually shown, Got it commits
the installed exact version through `UserExperienceService`'s existing atomic save.
Appearance and Update preferences also acknowledge before navigating. Failed writes retain
the previous decision and show an error. Later/title-bar Close and external GitHub navigation
do not acknowledge anything. Cached/network success is never an acknowledgement boundary.

Setup/upgrade choice remains the first automatic journey. Legacy-profile detection, saved
drafts, `DeferredWhatsNewVersion`, background/recovery suppression, active-work protection
and forward-version ordering remain in place. Explicit Skip defers notes without marking
them read. Manual reopening remains available; see [ONBOARDING.md](ONBOARDING.md).

## Help

Modern Help uses six native cards from the reviewed `Resources/HelpTopics.json`: getting
started, downloads/playlists, LIVE, Batch/Organizer, settings/updates and diagnostics/docs.
English and Polish content is supplied; other languages show an English-content notice.
Actions use existing page/dialog/settings callbacks. Documentation opens the official
repository; logs open the current profile's log folder. No shortcut silently starts a
recording/download or changes an option. Classic and About retain their existing actions.

## Validation boundaries

Catalog, migration, HTTP/cache and presentation tests separately cover exact versions,
candidate/stable ordering, English fallback, safe links, 200/304, unavailable or malformed
responses, limits, cache reuse, cancellation and durable acknowledgement. Deterministic
handlers provide invalid responses; those cases never target real GitHub releases.

Native window captures and test outcomes belong to the stable publication report. After
publication, a separate check must verify that official `/releases/tags/v1.3.0` content
actually reaches the running application's GitHub source state. A pre-publication 404 or
mock success is not evidence of that final check. This document describes implementation,
not a PASS ledger; physical DPI, long soak and other deferred qualifications remain distinct.
