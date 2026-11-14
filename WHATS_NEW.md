# What's New in the installed 1.3.0 candidate

`WhatsNewForm` is a native, scrollable ModernFormsNext window with Polish/English text,
shared appearance tokens, keyboard-accessible actions and stable automation IDs.
It uses controlled local content and works offline. It does not load HTML, remote
scripts or release-note text from arbitrary addresses.

The displayed version comes from the application's product version through
`AppBranding.Version` and `UserExperienceService`, with build metadata removed for
acknowledgement identity. It is not inferred solely from `AssemblyVersion`.
Prerelease suffixes remain visible. A `1.3.0-candidate.*` installation explicitly says
it is a preview build, not a published stable release. No release date is fabricated.

## Current local cards

The local `ReleaseNotesCatalog` supports the 1.3.0 family and selects three translated
cards from `Resources/Languages/pl.json` and `en.json`:

| Card | Implemented scope described |
| --- | --- |
| A workspace for every task | Modern Downloads, LIVE, History, Batch Import and Settings, details panels and shared actions; Classic remains available |
| Large collections, organized | Collapsible playlist groups, paged History and bounded thumbnail loading while retaining existing task IDs/records |
| Your preferences stay with you | Resumable setup, one-time interface selection and explicit update-policy controls; manual reopening of setup and notes |

These cards describe available functionality. They do not claim pixel-perfect visual
parity, a published release, resolved deferred native-automation issues, or completion
of full release qualification. Performance measurements remain in their dedicated
reports; the notes contain no invented benchmark numbers.

The official-release action opens the fixed URL
`https://github.com/ProGraMajster/ModernTubeDownloader/releases` in the user's browser.
It does not interpret a URL from the profile or render remote release HTML. The existing
updater's verified GitHub release notes remain a separate update-settings action.

## Display and acknowledgement rules

Automatic foreground startup chooses at most one journey: setup/upgrade choice first,
then What's New after successful completion. An existing user is never shown the
new-profile wizard merely because `experience.json` is absent. See
[ONBOARDING.md](ONBOARDING.md) for provenance and 1.2.1 migration.

The journey marker stores `LastObservedVersion`,
`LastWhatsNewAcknowledgedVersion`, and `DeferredWhatsNewVersion`. Acknowledgement is
written only after the window has actually been shown and the user invokes Got it,
Appearance, or Update preferences. The latter two actions acknowledge and navigate to
the corresponding existing Settings category. Opening the official releases page or
closing the window with X does not falsely record acceptance. A failed acknowledgement
write is reported and does not create a successful durable marker.

Skipping setup/upgrade choice defers the automatic notes for that exact version. It
does not record the notes as seen. Notes can always be explicitly reopened through
Settings, Help or About. Automatic presentation is suppressed for recovery/background
startup and external activation, and while restored queue/LIVE/Batch work is active.

Version comparisons ignore `+build` metadata, compare numeric core versions and compare
prerelease components with numeric ordering:

| Previously acknowledged | Current product version | Automatic notes |
| --- | --- | --- |
| 1.2.1 | 1.3.0-candidate.1 | Eligible for the candidate's local notes |
| 1.3.0-candidate.1 | 1.3.0-candidate.1+another-build | No repeat |
| 1.3.0-candidate.1 | 1.3.0-candidate.2 | Eligible if that build exists and has supported notes |
| 1.3.0-candidate.2 | 1.3.0 | Eligible for the stable version's notes |
| 1.3.0 | 1.3.0-candidate.3 | No downgrade prompt |
| 1.4.0 | 1.3.0 | No downgrade prompt |

The table specifies comparison behavior, not authorization to create another
candidate or stable release. Existing candidate versioning is unchanged by this work.
Neither unsuccessful installation nor a release merely discovered by the updater
acknowledges an uninstalled version.

## Evidence and maintenance

The native dialog offers **Later** as an explicit equivalent of closing without
acknowledgement. It does not mark the installed version as read. The 2026-11-12
native scenario verifies the unchanged experience marker (including SHA-256),
manual reopening, and subsequent explicit acknowledgement. The measured header,
feature cards and wrapping shortcuts share the setup dialog's native caption and
Downloads surfaces. See [current refinement evidence](FINAL_UI_REFINEMENT_REPORT.md).

Local note text must be updated only for implemented changes, and the supported version
family must be reviewed when the actual product version advances. The current window
uses native icons and text cards; it does not present the historical setup preview
assets as current qualification screenshots. Their exact provenance is documented in
[ONBOARDING.md](ONBOARDING.md).

The targeted `UserExperienceServiceTests` exercise acknowledgement, deferral and
candidate/stable/downgrade ordering. Native validation must separately demonstrate the
real dialog, scrolling, PL/EN, theme/text scaling, navigation and closing behavior.
The 2026-11-11 targeted run recorded 104/104 passing cases, including 29 journey-service
cases; exact scope, original log and TRX are linked from [ONBOARDING.md](ONBOARDING.md).
That run does not qualify the later dialog/layout changes or native interaction.
Record their actual results, failures and screenshots separately in the iteration report.
Full release qualification, merge,
tags, NuGet publication and application release remain outside this iteration's
authorization.
