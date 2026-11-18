# Versioned notes for the installed application

The native `WhatsNewForm` reads a reviewed embedded resource,
`ModernTubeDownloader/Resources/ReleaseNotes.json`. It has no HTTP dependency and
does not read changelog content or links from the user profile. The initial display
therefore does not wait for GitHub, a network timeout, rate limits or a missing tag.
No remote HTML, JavaScript or Markdown is interpreted inside the application.

## Catalog and provenance

Schema version 1 contains exact product versions, including their prerelease suffix.
Each version contains language records with four sections: `new`, `improved`,
`fixed` and `limitations`. Each section has a plain-text title and plain-text items.
The loader requires all four distinct categories and an English translation,
limits the resource to 256 KiB and 20 release records, limits each category to 12
items, and rejects blank/oversized text. The manifest is reviewed source code,
embedded in the application assembly, rather than an externally editable cache.

Only `1.3.0-candidate.1` currently has reviewed notes. No stable publication date,
future candidate features or older-version content is invented. The window shows
the installed version, a candidate notice, the local source and known qualification
limits. It does not claim a stable release or full release readiness.

Content is provided in Polish and English. Other interface languages use English
with a visible fallback notice; this is not represented as a complete translation
of release-note content into all 16 languages. Interface action labels continue to
use the application's localization service.

The displayed product version comes through `UserExperienceService` from
`AppBranding.Version`. Build metadata after `+` is excluded from release identity;
the prerelease suffix is retained. Catalog lookup is exact. For example,
`1.3.0-candidate.2` or stable `1.3.0` cannot borrow the first candidate's notes.
An explicitly reopened window for an unavailable record reports missing bundled
notes without claiming there were no changes, and closing it does not acknowledge
a changelog that was not present. Automatic notes require an exact catalog record.

There is currently only one local version; the UI does not show an empty archive
selector or fictitious older entries. Add reviewed records when maintaining a new
release. Keep the installed record, rather than a discovered update's version, as
the startup and acknowledgement target.

## Optional official information

The explicit external action constructs only this version-bound URL:

`https://github.com/ProGraMajster/ModernTubeDownloader/releases/tag/v{installed-version}`

The version must match the bounded ASCII version grammar. Query strings, path
separators, fragments and arbitrary URLs are rejected. Build metadata is removed.
The action opens the system browser; it neither downloads nor overwrites trusted
local notes. Candidate tags may legitimately return HTTP 404 because candidates
are not published releases. That browser result cannot block or change the local
window. The application does not invent an online synchronization timestamp.

The existing updater remains the sole owner of available-release discovery and
verified package installation. Its available-version description is separate from
the installed-version changelog. No second updater, network client, remote cache
or automatic fetch was added for What's New.

## Display and acknowledgement

The scrollable native window measures the header, all four section cards and
wrapping actions using the actual viewport, theme and text scale. Existing stable
automation IDs remain on the Later, Got it, Appearance and Updates actions.

Opening notes does not mark them as read. After the window has actually been shown,
Got it acknowledges the installed exact version through the existing atomic
`UserExperienceService` commit. Appearance and Update preferences do the same and
navigate to existing settings. Opening the browser does not acknowledge notes.
Later and the title-bar Close preserve the existing unacknowledged state, allowing
manual reopening. A failed acknowledgement write remains a failed operation and
does not publish a successful in-memory or durable decision.

Setup/upgrade choice remains the first automatic journey. Existing detection of
legacy profiles, saved drafts, `DeferredWhatsNewVersion`, background/recovery
suppression, active-work protection, and forward-version ordering are unchanged.
Skipping setup defers notes for that exact version without marking them as read.
See [ONBOARDING.md](ONBOARDING.md) for profile and migration boundaries.

## Help

Modern Help uses six native cards from the reviewed embedded
`Resources/HelpTopics.json`: getting started, downloads/playlists, LIVE,
Batch/Organizer, settings/updates and diagnostics/documentation. Polish and English
content is supplied; other languages show an explicit English-content notice.
Actions invoke the existing page, Batch dialog, Organizer, setup, notes and settings
callbacks. Documentation opens the official repository; logs open the existing
profile's log folder. No shortcut starts a download or mutates an option by itself.
Classic and About retain their existing presentation and actions.

## Validation boundaries

`ReleaseNotesCatalogTests` covers exact candidate matching, all four sections,
language fallback, another tag/available version and safe official links.
`ExperiencePresentationTests` covers the actual native control hierarchy, all
manifest text, responsive Help actions, Classic restoration, offline opening with
an unavailable HTTP handler, Later and durable acknowledgement. Existing service
tests retain failed-write, restart, unknown-field and queue/History preservation
assertions. Version ordering is tested independently of catalog availability.

Tests that configure an HTTP 404 handler assert that opening notes makes **no
request**; they do not claim a successful online fetch or a rendered remote release.
Native screenshots, executed test results and any failures belong to the iteration
report. This design document is not a PASS ledger. Physical DPI, owner visual
acceptance and final release qualification remain separate requirements.
