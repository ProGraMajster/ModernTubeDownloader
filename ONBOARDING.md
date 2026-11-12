# Setup and migration to the 1.3.0 candidate

The native setup journey uses the existing `AppServices`, `SettingsService`,
`AppAppearanceService` and `ApplicationUpdateService`. Classic and Modern continue to
share one queue, history, LIVE runtime and profile. Completing setup does not create a
parallel data store or replace the existing task models.

This document describes implementation, not release qualification. The product remains
a **1.3.0 candidate**. Test results and native captures belong in the iteration report;
the presence of the code or previews below is not a PASS claim.

## Deciding which startup flow to show

`UserExperienceService` opens before tool initialization can create the first settings
file. It uses `<profile>/experience.json`, schema version 1. When this marker is absent,
an existing `settings.json`, `appearance.json`, `queue.json`, `history.json`, or
`live-sessions.json`, or any entry in `Auth` or `Batch`, identifies an existing profile.
Detection checks existence only; it does not deserialize queue/history or inspect
cookie contents. A missing journey marker does not mean a new user.
Metadata access distinguishes missing entries from I/O/permission failures. A failure
while probing profile evidence or creating the first marker suppresses the journey,
conservatively treats the profile as existing, and is logged without aborting startup.

| Profile state | Automatic foreground behavior |
| --- | --- |
| No marker and no existing profile evidence | Six-step welcome wizard |
| New profile with setup `InProgress` | Resume its saved draft and step |
| New profile with setup `Completed` or `Skipped` | Do not repeat the welcome wizard |
| Existing Classic profile without a 1.3 choice marker | One-time interface/update-policy chooser |
| Existing Modern profile with a clear update policy | Preserve Modern; no redundant appearance chooser |
| Existing Modern profile with ambiguous legacy update policy | Chooser retains Modern and offers an explicit policy decision |
| Unreadable or unsupported journey marker | Suppress automatic prompts and retain the original file |

The modal journey starts after the UI/runtime preparation is ready. Automatic prompts
are suppressed for `--classic-ui` recovery, hidden/autostart startup, external URI or
notification activation, and active queue/LIVE/Batch work. They are not retried in a
loop during the same application session. Setup and What's New remain explicitly
available from Settings, Help and About.

## Welcome wizard

The native `SetupWizardForm` has these steps in Polish and English:

1. Welcome and language.
2. Modern Dashboard or Classic, with actual sample application views.
3. Output/temp folders, filename template and conflict behavior.
4. Existing quality preset and preferred container.
5. Automatic update policy, notifications and tray icon.
6. Summary and explicit completion.

Modern is recommended for a genuinely new profile. The choice remains reversible in
Appearance settings. Existing users retain their current interface as the chooser's
initial selection. The wizard does not enable cookies, account access, Web Remote,
autostart, or new background integrations.

Folder selection uses the native folder browser. Validation requires absolute paths,
rejects paths occupied by files, and probes writability in the nearest existing parent.
It does not create the selected output/temp directories merely to validate them.
Filename validation rejects empty values and control-line separators. Source format
availability still constrains download quality/container choices.

## Saved progress and interrupted setup

`experience.json` stores `ExistingProfile`, `Setup`, the current `Draft`, the 1.3 interface
choice marker, version observations and What's New acknowledgement/deferral. Setup
states are `NotStarted`, `InProgress`, `Completed`, and explicit `Skipped` (serialized
enum values 0–3). Draft edits and navigation use the existing atomic JSON writer.

Closing or interrupting the wizard does not mark it completed. A successful draft save
allows a later start to resume; if a write fails, the last durable marker remains the
recovery point. Explicit Skip clears the draft, records the skip, and defers automatic
What's New for that exact version without falsely acknowledging it. Manual reopening
is supported after completion or skip and does not delete user data.

Only Finish/Apply changes preferences. The sequence is:

1. Validate folders for the full wizard and await pending draft saves.
2. Apply selected settings through the existing settings/update service.
3. Await the existing appearance profile API to save the chosen shell.
4. Apply the selected language, then persist completion in the journey marker.

These are independent atomic stores, not a cross-file transaction. An interruption
between them can leave preferences already applied with the journey still InProgress;
repeating completion is idempotent. Completion is never written before those saves
finish. Upgrade mode changes only interface/update preferences, not output paths,
quality, cookies or other legacy choices.

Malformed journey state is preserved. Explicitly reopening setup can recover it only
after copying its exact original bytes to `experience.json.preserved-<unique id>`.
A marker with a newer schema is not rewritten, including on manual setup. Unknown
schema-1 marker properties round-trip through JSON extension data.
The required `schemaVersion`, `existingProfile` and `setup` fields must actually exist
in its JSON. An empty or incomplete object is damaged evidence, never proof of a new
profile supplied by deserialization defaults.
An unreadable marker is not assumed to contain malformed JSON: storage/permission
failures prohibit manual replacement in that session; reopening the application after
repair re-evaluates it. Atomic marker writes use unique temporary filenames and do not
overwrite an older interrupted `experience.json.tmp` artifact. Invalid draft enum
values discard only the in-memory draft; language, quality and null text fields receive
safe UI values. Loading/normalizing a draft does not rewrite the source marker.

## Safe migration from 1.2.1

Migration identifies profile provenance before applying new defaults. It neither
renumbers tasks nor rewrites history, queue, cookies or LIVE records to add onboarding
information. Those files continue to be owned by their original runtime services.
Large collections therefore do not become wizard rows, bitmap previews or duplicated
in-memory models.

`SettingsService` distinguishes original JSON fields from values supplied by new CLR
defaults:

| Existing update settings | Result before explicit completion |
| --- | --- |
| `AutomaticChecks=false` | Preserve disabled checks; do not opt in |
| Existing valid AutoDownload or InstallOnExit | Preserve that mode |
| Confirmed NotifyOnly (`PolicyVersion=1`) | Preserve the decision |
| Missing mode, or legacy NotifyOnly without policy provenance | Keep NotifyOnly and request a clear choice |
| Existing data but no settings file | Treat as legacy, using conservative NotifyOnly |
| Corrupt/unreadable settings | Disable automatic checks, use NotifyOnly in memory, refuse routine saves over the original file |

The ambiguous-policy chooser recommends automatic verified staging/install-on-exit,
but that recommendation is a draft until the user completes the chooser. Confirmed
choices use `PolicyVersion=1`. Explicitly disabled checks and persisted skipped/deferred
versions remain respected. New profiles use automatic startup/daily checks and
InstallOnExit. See [UPDATES.md](UPDATES.md) for staging, shutdown and rollback behavior.

Unknown top-level settings and updater-policy JSON properties are preserved when known
preferences are saved. Unreadable settings cannot be silently replaced by defaults at
shutdown. Cancellation is propagated rather than classified as file corruption.

## Preview asset provenance

The embedded thumbnails are historical native application captures with isolated
synthetic QA content. They are copied byte-for-byte, without resizing or cropping:

| Embedded asset | Original capture |
| --- | --- |
| `ModernTubeDownloader/Assets/PreviewClassic.png` | `artifacts/stability-completion-20261108/verified-classic-scan/startup.png` |
| `ModernTubeDownloader/Assets/PreviewModern.png` | `artifacts/mfn-master-integration-20261108/acceptance-captures/accept-playlist-native-pl/profile-1/downloads-two-playlists-and-singles.png` |

`PictureBoxSizeMode.Zoom` fits them inside the interface cards. The UI labels them as
actual views with sample data. They are not generated mockups, live previews of the
current user's data, or evidence that the current working tree passed visual QA.
Current wizard/chooser captures must be recorded separately in the iteration report.
SHA-256 equality was checked between each embedded asset and its source: Classic
`32CD4EED3719C33E89734E6486E6388D893409FFD429AFCB97B467BAE5F0896C`, Modern
`AD6810AD6F31D3360C30BB1A231D145512EE423E78264DE263ED65195DEDC5DF`.

## Targeted validation

Relevant suites are `UserExperienceServiceTests`, `SettingsMigrationTests`,
`ApplicationUpdateTests`, and the setup/update presentation tests. They cover resume,
skip, completion, source-data locks, 10,000 preserved IDs and byte hashes, malformed and
future markers, policy provenance, and candidate/stable version ordering. The updater
suite additionally launches the real updater against synthetic installations.

The targeted Debug run on 2026-11-11 passed **104/104, 0 failed, 0 skipped** using the
isolated .NET 10.0.401 SDK. Evidence is
[`journey-updates-03.log`](artifacts/onboarding-dashboard-20261111/journey-updates-03.log)
and [`journey-updates-03.trx`](artifacts/onboarding-dashboard-20261111/test-results/journey-updates-03.trx).
That exact run contained 52 `ApplicationUpdateTests`, 20 `SettingsMigrationTests`,
3 `UpdateSettingsPresentationTests`, and 29 `UserExperienceServiceTests`.
The filter was:

```text
FullyQualifiedName~ApplicationUpdateTests|FullyQualifiedName~UpdateSettingsPresentationTests|FullyQualifiedName~UserExperienceServiceTests|FullyQualifiedName~SettingsMigrationTests
```

This result applies to that targeted run, not later UI edits or full-release testing.
The subsequent `presentation-03` run executed **43/43 `UserExperienceServiceTests`
successfully**, including the fourteen added journey-hardening cases for initial-storage
failures, locked markers, evidence paths, interrupted-write retention and invalid/null
drafts. The complete run nevertheless **FAILED: 57 passed, 7 failed, 0 skipped, 64 total**.
The seven failures were in `ExperiencePresentationTests`, before the headless dispatcher,
async lifecycle and diagnostic file-sharing corrections; the original results contain
`IOException` failures involving open log files or temporary marker files. Both the
[`presentation-03.log`](artifacts/onboarding-dashboard-20261111/presentation-03.log) and
[`presentation-03.trx`](artifacts/onboarding-dashboard-20261111/test-results/presentation-03.trx)
are retained as failed-run evidence, not presented as a successful complete run.

After those corrections and the final presentation fixes, `presentation-04` passed
**50/50, 0 failed, 0 skipped**. The raw results contain 8 `ExperiencePresentationTests`,
8 `BatchDashboardPresentationTests`, 7 `HistoryDashboardPresentationTests`,
21 `LiveDashboardPresentationTests`, and 6 `SettingsDashboardPresentationTests`.
The experience cases include all six wizard steps and What's New at both window sizes
in Dark/Light at 100/150/200% text scale, durable completion, folder validation and the
existing-user interface choice. Evidence is
[`presentation-04.log`](artifacts/onboarding-dashboard-20261111/presentation-04.log) and
[`presentation-04.trx`](artifacts/onboarding-dashboard-20261111/test-results/presentation-04.trx).
This presentation run did not rerun `UserExperienceServiceTests` or the updater/migration
suites; their results remain attributed to the separate runs above. These targeted
headless results do not constitute native Windows UIA or physical DPI qualification.

The final targeted safety run `journey-final-01` passed **147/147** after serialized
updater initialization, corrupt-state protection and native viewport fixes.
`marker-final-01` passed **47/47** service cases after adding incomplete-marker checks;
`experience-final-01` passed **9/9** presentation cases after final theme corrections.
These overlapping runs are not additive. See the
[iteration report](docs/onboarding-dashboard-20261111.md) for the final native outcomes,
captured screens, prior failures and qualification limits.

Native acceptance must separately demonstrate
wizard steps, chooser, resume/skip, shell switching and external/background suppression
in the actual running application. Full Debug/Release/JS/UIA/DPI/5K/performance/package
qualification remains the release gate; the previously deferred intermittent UIA
cases remain **DEFERRED / NOT QUALIFIED** unless separately evidenced otherwise.

The [2026-11-12 refinement report](FINAL_UI_REFINEMENT_REPORT.md) records the newer
native six-step/summary captures, both upgrade choices, resume/restart, and targeted
error-layout/palette tests. Dialogs now share measured cards and the existing Downloads
palette even before Modern is chosen. Existing Classic colors and the persisted journey
contract remain unchanged. Original functional PASS captures with the missing palette
are retained as visual failures; they are not current visual acceptance evidence.
