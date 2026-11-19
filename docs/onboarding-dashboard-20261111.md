# Modern Dashboard, onboarding and update policy — 2026-11-11

This is a **partial qualification report for `1.3.0-candidate.1`**, not release approval or a claim of pixel-perfect reference parity. Implementation and targeted verification were performed in the existing `feature/modern-dashboard-ui` worktree, from baseline `ffa1d8e91f201220f323624519d4c7bf50956f68`. ModernFormsNext remains centrally pinned to merged master `7bf58d06d795d7298c86d9bf9471bbfb46852bab`.

Classic and Modern still use the same settings, queue, History, LIVE, Batch and thumbnail services. No second runtime, importer, updater or private-data store was introduced. `experience.json` contains journey markers and draft preferences only. The main checkout, framework sources, existing worktrees and historical reports are outside this iteration's write scope. No application version change, tag, release, NuGet publication or merge of Draft PR #3 is authorized or claimed.

## Completed implementation

### Five dashboard screens

- **LIVE:** denser rows, consistent source/status/action presentation and a structured inspector with real recording facts, output path and contextual actions. A new native session-details window groups recording metadata, connection/recovery information and saved parts, with scrolling, bounded shared-cache thumbnails, copy/open/stop/resume actions and commands resolved against the canonical session at invocation. Existing provider filters, search, sorting, paging and recording ownership remain in place.
- **History:** tighter table columns and inspector, status badge, selectable/copyable output path and four real row-menu actions: open file, open folder, copy source and retry. Keyboard and pointer selection remain supported. A reused page row refuses actions from a menu opened for its previous record. The existing reusable 40-row pager, direct/first/last navigation and bounded thumbnail loading remain intact.
- **Batch Import:** revised title/source strip, file drop/picker card, real parser counters, status details, compact source/title/quality/status/action columns and bottom summary. Desktop rows are denser; large text and narrow windows use stacked rows. The optional thumbnail switch saves through the existing appearance preference and preserves a previously disabled value. Available metadata uses the shared lifetime-bound thumbnail cache; unavailable images use an honest placeholder. TXT/CSV/JSON, clipboard input, deduplication, filtering, paging and explicit handoff use the existing importer. Valid URL counters describe syntax/identity; they are not a promise that every source will download successfully.
- **Other:** the canonical Batch file workflow is the main card. Existing thumbnail tooling and routes to History, Settings and Help form the tool grid. Quick actions open the output/log folders, export the appearance profile and check updates. Recent operations show actual saved Batch sessions and thumbnail-export records; an empty history remains empty. No conversion, subtitle editor, merger, fictional status or estimated throughput was added to imitate the reference.
- **Settings:** all eleven categories remain. Modern cards use measured labels and responsive rows for paths, download behavior, LIVE, browser integration, remote access and advanced options. Category search and navigation remain available. Hidden cards clear their bounds; the page no longer intentionally retains a wide Classic allocation. General retains its responsive overview and links to complete editors. Setup and What's New are available from Settings, Help and About through the existing shell routes.

Shared Downloads colors, gradients, native vector icons, typography and card/button recipes are reused. Classic layout and typography restoration remains explicit; visual improvements do not replace the native UI with HTML or a WebView.

### First-run, migration and What's New

The native six-step wizard provides PL/EN, explicit Classic/Modern selection, actual historical application preview images, folder/filename settings, quality/container, automatic updates, notifications/tray and an explicit summary/completion step. Native folder dialogs and writable-path validation use the existing defaults and preference APIs. Selecting a future output directory does not create it merely to validate the path.

The versioned journey marker persists draft edits and navigation atomically. Closing a wizard does not mark setup complete. Completion waits for preferences and appearance to be saved before recording success. These are separate atomic files, not a cross-file transaction; interruption between saves can leave some preferences already applied while the journey remains resumable. Completion is idempotent. Skip is an explicit durable decision. Reopening setup does not erase application data.

Existing-profile detection runs before initialization can create the first settings file. Presence of prior settings, appearance, queue, History, LIVE, Auth or Batch evidence identifies an existing user without deserializing large collections or reading cookie contents. A missing new marker alone never means a new user. Storage/permission errors fail conservatively: preserve files, suppress automatic prompts and log the failure. Malformed markers require preservation of original bytes before explicit recovery; unsupported future schemas are not rewritten. Unknown marker and supported settings/update-policy properties round-trip.

The final hardening also rejects a partially shaped marker lacking `schemaVersion`, `existingProfile` or `setup`; deserialization defaults cannot turn incomplete provenance into a fresh profile. Settings are read directly, treating only a genuine missing file/directory as absence, because `File.Exists` can also hide access failures. Locked/unreadable settings remain protected. The startup error dialog uses `MessageBoxForm`, is registered as the active journey modal and can be closed by the same external-activation/shutdown suspension path as the wizard, avoiding an untracked modal wait.

Existing Classic users receive a one-time interface/update-policy choice rather than the new-profile wizard. Existing Modern selection remains selected; a redundant appearance decision is avoided when the update policy is already clear. Setup and upgrade flow then lead to local What's New content. Candidate suffixes remain visible; no stable release date or unimplemented changelog entry is invented. Acknowledgement is recorded only after actual display and an accepting action. Closing the notes or discovering a future update does not acknowledge that uninstalled version.

Automatic modal presentation is suppressed for recovery `--classic-ui`, hidden/autostart, external activation and active queue/LIVE/Batch work. New setup never enables cookies, account access, Web Remote or autostart silently. Manual entry points remain available. Detailed contracts and preview provenance: [ONBOARDING.md](../ONBOARDING.md), [WHATS_NEW.md](../WHATS_NEW.md), [UPDATES.md](../UPDATES.md), [MODERN_DASHBOARD_UI.md](../MODERN_DASHBOARD_UI.md).

### Automatic updates and 1.2.1 migration

New profiles default to `AutomaticChecks=true`, `StartupDaily`, `InstallOnExit`. This means asynchronous check → download → verification → staging → installation only after a genuine safe application exit. It does not mean restart when the package becomes ready, restart during recording, or installation on Hide-to-tray.

Migration examines the original JSON before CLR defaults can blur missing fields and explicit choices:

| Existing evidence | Behavior before explicit user confirmation |
| --- | --- |
| `AutomaticChecks=false` | Preserve disabled checks |
| Existing AutoDownload or InstallOnExit | Preserve the mode |
| NotifyOnly with `PolicyVersion=1` | Preserve confirmed NotifyOnly |
| Missing mode or ambiguous old NotifyOnly | Retain conservative NotifyOnly; offer a visible automatic-update choice |
| Existing private data without settings | Treat as legacy; do not silently opt in |
| Unreadable/corrupt settings | Preserve the source; disable automatic checks in memory and refuse routine overwrite |

The upgrade chooser recommends automatic install-on-exit only as a **draft**. Accepting the chooser records the explicit policy using the existing settings service and `PolicyVersion=1`. Recognizable opt-outs, deferred/skipped versions and a package's manually disabled install intent survive monitor cycles and restart. Saving policy durably disarms the prepared package first; a failed settings write cannot leave it automatically armed.

The existing updater accepts official stable repository/tag/assets, bounded valid ZIP content and matching SHA-256, then revalidates in its separate process. SHA-256 is an integrity check, not a publisher signature. Staging is reused only after verification. Automatic preparation neither launches the installer nor requests shutdown. The normal shutdown path coordinates Batch/queue/LIVE and durable saves before invoking the updater, which waits for the actual parent PID/start-time identity to exit. It never updates Auth, queue, History, LIVE, downloads, settings or appearance files and does not request elevation.

Updater initialization is serialized and completed before policy mutation. A delayed monitor cannot reload an older armed snapshot over a new opt-out, and repeated initialization does not duplicate notifications. Unreadable state fails closed for automatic/manual package mutation and remains preserved, while disabling checks is still possible. Initialization now notifies an already-created Settings panel when a restored, verified archive is ready, including when automatic checks are disabled.

The real updater-process tests passed for parent-exit waiting/install and for mid-copy failure caused by a directory obstruction, read-only destination and locked file. They verify rollback and preservation of private/unlisted data in isolated synthetic installations. Recovery continues restoring other files if one changed file remains locked, retaining an incomplete journal for later recovery rather than claiming success. A failed or disappeared installer is surfaced on the next start and is not silently relaunched on every exit. These are genuine separate-process tests, **not an update of the user's installed application**.

## Diagnosed causes and corrections

| Finding | Cause / correction | Evidence status at this checkpoint |
| --- | --- | --- |
| Legacy users could be mistaken for a new profile | A new marker cannot establish provenance; probe existing profile evidence before startup writes settings | Service and migration tests PASS |
| Automatic defaults could overwrite ambiguous old intent | Deserialize-after-defaults loses presence information; inspect source fields and require explicit policy confirmation | Targeted tests PASS |
| A prepared package could remain armed after policy changes | Install intent must be persisted/disarmed before applying policy; monitor and restore paths must respect opt-out/deferral | Targeted updater tests PASS |
| Rollback could stop at an unchanged or locked destination | Recover other changed files, skip intact unchanged files and retain incomplete journal when necessary | Real-process and transaction tests PASS |
| Hidden Startup Settings card retained hundreds of pixels of extent | `SizeChanged` relaid out a zero-width hidden card and recreated its height; ignore that zero-width allocation | Settings 100/150/200% tests PASS |
| Batch page title disappeared; toolbar showed only Add | Fixed title allocation was shorter than the Modern font; toolbar width depended on child positions already wrapped at the old width | Measured heading + sum of action widths/margins; final targeted/native Batch PASS |
| Initial wizard component tests failed although native New passed | Headless actions bypassed the production dispatcher synchronization context; diagnostic code also tried to read open log files without sharing | Dispatch actions through posted UI work and await lifecycle; diagnostics use shared reads only on error; presentation-04 PASS |
| Large-text resume / upgrade chooser had horizontal overflow | Native scroll extent exceeded the client viewport by approximately 5 px; final width allocation now accounts for the actual scrollbar viewport | Original FAIL retained; Resume Dark200 and both upgrade `-02` native reruns PASS |
| English News / external activation native lanes stopped | Harness used Modern `NavHelp` for a Classic shell and an invalid URI-v1 fixture | Corrected harness; News EN and Resume External `-02` native reruns PASS |
| Restored staged package remained visually “not checked” | Update initialization changed service status without notifying the already-created UI | Notification correction, regression test and native update-states `-02` PASS |
| Settings/update marker safety during startup | Missing fields, access errors or policy mutation before initialization could be confused with fresh/default state | Explicit required-marker fields, direct settings reads and serialized fail-closed updater initialization; final targeted tests PASS |
| Grey native wizard input backgrounds / duplicate chooser hint | Child input styles were not bound consistently to the current theme; the same explanatory hint was repeated | Final theme binding and duplicate removal; 9/9 presentation tests and three native recaptures PASS |

These targeted corrections do not resolve the separately deferred general UIA issues. No assertion has been relaxed, test skipped or timeout raised to turn a failure green.

## Targeted automated evidence

The runs below overlap. **Do not add their counts as unique tests**, and do not transfer a PASS to source edits made after a run. Raw TRX counters and log summaries were checked for this report. Builds used the isolated .NET 10.0.401 SDK because the system SDK inventory no longer satisfied the repository's .NET 10 requirement; the repository's SDK contract was not changed to conceal that environment issue.

| Run | Scope and exact result | Raw evidence |
| --- | --- | --- |
| `journey-updates-03` | **104 PASS / 0 FAIL / 0 skipped**: 52 ApplicationUpdate, 20 SettingsMigration, 3 UpdateSettingsPresentation, 29 UserExperienceService | [log](../artifacts/onboarding-dashboard-20261111/journey-updates-03.log), [TRX](../artifacts/onboarding-dashboard-20261111/test-results/journey-updates-03.trx) |
| `presentation-03` | **43/43 UserExperienceService PASS**, including 14 additional hardening cases; whole run still **57 PASS / 7 FAIL / 0 skipped** | [log](../artifacts/onboarding-dashboard-20261111/presentation-03.log), [TRX](../artifacts/onboarding-dashboard-20261111/test-results/presentation-03.trx) |
| `presentation-04` | **50 PASS / 0 FAIL / 0 skipped**: Experience8, Batch8, History7, LIVE21, Settings6 | [log](../artifacts/onboarding-dashboard-20261111/presentation-04.log), [TRX](../artifacts/onboarding-dashboard-20261111/test-results/presentation-04.trx) |
| `journey-final-01` | **147 PASS / 0 FAIL / 0 skipped**: ApplicationUpdate56, ExperiencePresentation9, SettingsMigration22, StartupAndTray14, UpdateSettingsPresentation3, UserExperienceService43 | [log](../artifacts/onboarding-dashboard-20261111/journey-final-01.log), [TRX](../artifacts/onboarding-dashboard-20261111/test-results/journey-final-01.trx) |
| `marker-final-01` | **47 PASS / 0 FAIL / 0 skipped**: UserExperienceService, including four additional incomplete-marker cases | [log](../artifacts/onboarding-dashboard-20261111/marker-final-01.log), [TRX](../artifacts/onboarding-dashboard-20261111/test-results/marker-final-01.trx) |
| `experience-final-01` | **9 PASS / 0 FAIL / 0 skipped**, final wizard theme and chooser geometry | [log](../artifacts/onboarding-dashboard-20261111/experience-final-01.log), [TRX](../artifacts/onboarding-dashboard-20261111/test-results/experience-final-01.trx) |

The 43 service cases cover marker/draft persistence, explicit skip, manual reopening, read/initial-write failures, future/malformed/locked markers, legacy evidence, enum/null draft normalization, preserved unknown fields and candidate/stable ordering. The large migration fixture checks **10,000 task identities and exact private-store bytes**. It is a data-preservation test, not a fresh native startup/RAM benchmark.

Presentation tests cover six wizard steps, both themes, 100/150/200% application text, two window sizes, real preview assets, PL/EN changes, folder validation, durable completion and upgrade preferences. Batch/Other checks exercise actual importer metadata and working tool routes, thumbnail preference persistence, Classic restoration and responsive geometry. Settings checks include narrow/wide category transitions and strict empty bounds for hidden cards. LIVE/History cover bounded pages, current action targets and inspector behavior.

Retained unsuccessful attempts:

- [`journey-updates-01.log`](../artifacts/onboarding-dashboard-20261111/journey-updates-01.log): required SDK unavailable; no test PASS claimed.
- [`journey-updates-02.log`](../artifacts/onboarding-dashboard-20261111/journey-updates-02.log): build errors passing `Form` to APIs requiring `Control`; dialogs now use their root control surfaces.
- [`presentation-01.log`](../artifacts/onboarding-dashboard-20261111/presentation-01.log): **38 PASS / 4 FAIL**; hidden Settings extent plus History headless interaction.
- [`presentation-02.log`](../artifacts/onboarding-dashboard-20261111/presentation-02.log): **11 PASS / 10 FAIL**; pre-guard Settings failures and wizard driver/lifecycle failures.
- `presentation-03`: **57 PASS / 7 FAIL**, retained in full. Its seven failed Experience cases include open-log or temporary-marker `IOException`; its 43 service successes do not make the complete run successful.

## Native captures and targeted interactions

These are actual application HWND captures and targeted Debug semantic-bridge interactions on isolated synthetic profiles, **not full external Windows UI Automation or physical-DPI qualification**. Dashboard lanes captured actual 1448×1086 windows at HWND DPI96. Application text scale is recorded separately from Windows DPI. Normal-startup checks in the screen lanes verified that the bridge was not exposed and the application shut down cleanly. Indicative HWND/input-idle timings in logs are not cold startup, tools-ready or performance-regression measurements.

| Lane | Result and scope | Evidence / capture |
| --- | --- | --- |
| New, Dark100 | **PASS**: six steps, completion, local News and next start | [log](../artifacts/onboarding-dashboard-20261111/native-new-dark-polish.log); archived individual steps below |
| New, Light150 | **PASS**: targeted complete new-profile flow | [log](../artifacts/onboarding-dashboard-20261111/native-new-light150-polish.log) |
| Manual reopen | **PASS**: reopen setup through application entry point | [log](../artifacts/onboarding-dashboard-20261111/native-reopen.log) |
| LIVE | **PASS**: current/scheduled selection, empty provider filter, search, recent; native session-details capture also retained | [final log](../artifacts/onboarding-dashboard-20261111/native-live-final.log), [current](../artifacts/onboarding-dashboard-20261111/native/native-live-final/live-current.png), [details](../artifacts/onboarding-dashboard-20261111/native/native-live-final/live-details.png) |
| History | **PASS**: selected record and inspector | [final log](../artifacts/onboarding-dashboard-20261111/native-history-final.log), [capture](../artifacts/onboarding-dashboard-20261111/native/native-history-final/history-selected.png) |
| Batch | **PASS**: parsed and analyzed canonical import after title/toolbar corrections | [final log](../artifacts/onboarding-dashboard-20261111/native-batch-final.log), [parsed](../artifacts/onboarding-dashboard-20261111/native/native-batch-final/batch-import-parsed.png), [analyzed](../artifacts/onboarding-dashboard-20261111/native/native-batch-final/batch-import-analyzed.png) |
| Other | **PASS**: real tool composition and truthful empty recent list in this fixture | [log](../artifacts/onboarding-dashboard-20261111/native-screen-other-01.log), [capture](../artifacts/onboarding-dashboard-20261111/native/native-screen-other-01/other-tools.png) |
| Settings, Dark100 | **PASS**: General and search-to-Downloads | [log](../artifacts/onboarding-dashboard-20261111/native-screen-settings-01.log), [General](../artifacts/onboarding-dashboard-20261111/native/native-screen-settings-01/settings-general.png), [search](../artifacts/onboarding-dashboard-20261111/native/native-screen-settings-01/settings-search-downloads.png) |
| Settings, Light150 | **PASS**: same targeted responsive path | [log](../artifacts/onboarding-dashboard-20261111/native-settings-light150.log) |
| Resume Dark200; upgrade Classic/Modern | Corrected `-02` lanes **PASS**; original overflow failures retained | PASS: [resume](../artifacts/onboarding-dashboard-20261111/native-resume-dark200-02.log), [Classic](../artifacts/onboarding-dashboard-20261111/native-upgrade-classic-02.log), [Modern](../artifacts/onboarding-dashboard-20261111/native-upgrade-modern-02.log); original FAIL: [resume](../artifacts/onboarding-dashboard-20261111/native-resume-dark200.log), [Classic](../artifacts/onboarding-dashboard-20261111/native-upgrade-classic.log), [Modern](../artifacts/onboarding-dashboard-20261111/native-upgrade-modern.log) |
| News EN; external URI | Corrected harness `-02` lanes **PASS**; original failures retained | PASS: [News](../artifacts/onboarding-dashboard-20261111/native-news-en-02.log), [URI](../artifacts/onboarding-dashboard-20261111/native-resume-external-02.log); original FAIL: [News](../artifacts/onboarding-dashboard-20261111/native-news-en.log), [URI](../artifacts/onboarding-dashboard-20261111/native-resume-external.log) |
| Update on/off/staged | `-02` **PASS**: explicit policy off/on, verified restored package and safe deferral; no installation or public release download invoked by this UI fixture | [PASS log](../artifacts/onboarding-dashboard-20261111/native-update-states-02.log); [original FAIL](../artifacts/onboarding-dashboard-20261111/native-update-states.log) |

The original journey PNGs and semantic trees have been copied from their owned temporary profiles to per-log artifact folders by `Collect-Evidence.ps1`; source profile paths remain in `PROFILE.txt` and the logs. They are **not** the embedded historical preview assets. The [gallery](../artifacts/onboarding-dashboard-20261111/gallery.html) places the five supplied references beside actual captures; the [native manifest](../artifacts/onboarding-dashboard-20261111/native-manifest.json) records scenario status and image names, including failed lanes.

Archived Dark100 captures: [step1](../artifacts/onboarding-dashboard-20261111/native/native-new-dark-polish/experience-step-1.png), [step2](../artifacts/onboarding-dashboard-20261111/native/native-new-dark-polish/experience-step-2.png), [step3](../artifacts/onboarding-dashboard-20261111/native/native-new-dark-polish/experience-step-3.png), [step4](../artifacts/onboarding-dashboard-20261111/native/native-new-dark-polish/experience-step-4.png), [step5](../artifacts/onboarding-dashboard-20261111/native/native-new-dark-polish/experience-step-5.png), [step6](../artifacts/onboarding-dashboard-20261111/native/native-new-dark-polish/experience-step-6.png), [What's New](../artifacts/onboarding-dashboard-20261111/native/native-new-dark-polish/experience-first-run-news.png). The corresponding Light150 and Reopen captures are in [native-new-light150-polish](../artifacts/onboarding-dashboard-20261111/native/native-new-light150-polish/) and [native-reopen](../artifacts/onboarding-dashboard-20261111/native/native-reopen/).

## Required ten-flow matrix

PASS means only the stated evidence passed. PARTIAL means a required native segment or environment remains unqualified. PENDING identifies an active targeted correction/rerun; it is not a hidden PASS. None of these rows grants release approval.

| # | Requested flow | Status | Demonstrated evidence and remaining boundary |
| --- | --- | --- | --- |
| 1 | New profile → wizard → chosen shell → save → no wizard after restart | **PASS — targeted** | New Dark100 and Light150 native lanes plus durable service/presentation tests; full physical-DPI/keyboard matrix remains deferred |
| 2 | Interrupted new setup → restart → resume or explicit skip | **PASS — targeted** | Draft/skip/resume service tests and corrected native Resume Dark200 PASS; final theme-only geometry tests and native recaptures also PASS |
| 3 | Existing 1.2.1, large private stores → choice → News → no data loss | **PASS — targeted** | 10,000 identity/byte-preservation service fixture and legacy detection PASS; both corrected native chooser flows PASS. These are separate evidence scopes, not a native 10,000-item latency test |
| 4 | Existing Modern choice survives upgrade without a modal loop | **PASS — targeted service contract** | Confirmed-policy and ambiguous-policy cases PASS; native upgrade-to-Modern also PASS, but it does not substitute for every historical candidate-profile variant |
| 5 | Disabled updates or deferred/skipped version survive migration | **PASS — targeted** | SettingsMigration and updater policy tests PASS, including restart and disarmed prepared-package intent; UI on/off/staged acceptance is separate in row6 |
| 6 | Automatic check/download/stage/install-on-exit while idle | **PASS — targeted** | Daily/reuse/no-installer-during-preparation contracts, real updater parent-exit/install and corrected native on/off/staged/deferral UI PASS; full packaged application shutdown remains part of row7/final qualification |
| 7 | Active queue/LIVE, tray, exit, crash and inaccessible installation | **PARTIAL** | Separate-process rollback for locked/read-only/directory failures and preserved data PASS; full active-work/tray/crash shutdown integration matrix remains DEFERRED |
| 8 | Offline, bad GitHub response, checksum/ZIP failure | **PASS — targeted** | Existing update tests reject unsafe metadata/archive/checksum and expose safe failures without installation; no real user's installation or uncontrolled network failure was used |
| 9 | Repeated startup / old JSON migration remains idempotent | **PASS — targeted** | Service and migration tests preserve private bytes/IDs, explicit policy, unknown fields and restart markers; not a new memory/latency benchmark |
| 10 | `--classic-ui`, hidden/autostart, external URI, notification | **PARTIAL** | Service suppression contracts and StartupAndTray tests PASS; corrected News/Classic routing and native external-URI interruption/resume PASS; full notification/OS-startup external UIA remains DEFERRED |

## Visual comparison and remaining differences

The original `LIVE.png`, `Historia.png`, `Batch Import.png`, `Pozostałe.png` and `Ustawienia.png` remain the visual targets; Downloads remains the accepted shared style direction. Native screenshots support the table/inspector, source-strip, card and action-layout comparisons. They do **not** establish a pixel-difference score or pixel-perfect equivalence. Synthetic titles/thumbnails intentionally differ from the reference media.

- LIVE now uses compact stateful rows and structured details, but no fabricated chat, viewers, likes or unsupported recording settings appear. Reference-only integrations remain absent.
- History retains the table/inspector relationship, actual metadata and contextual actions. Notes and reference-only bulk operations were not invented. Page size and materialization bounds follow the existing runtime rather than the example screenshot's row count.
- Batch exposes actual parser/analysis states and optional real thumbnails. No fictional destination/profile column or estimated throughput is shown where the importer has no canonical value. The reference's very dense single toolbar can wrap at large text; actions remain available through the responsive layout.
- Other deliberately shows fewer tool types than the reference because conversion/subtitle/merge modules do not exist in the agreed scope. Its recent list only shows actual records. Tile descriptions are bounded to three lines; unusually long localized text at narrow widths still needs final visual review.
- Settings preserves all eleven categories and uses overview summaries that link to full editors. It is not a static replica of a single reference category. The nested `CookieSyncSettingsPanel` still has legacy fixed internal coordinates and is **NOT QUALIFIED at 150/200% text**, even though its containing card is responsive. Final advanced-category screenshots must cover this limitation.
- Upgrade/resume horizontal overflow and restored update-status notification passed their targeted `-02` reruns; the original FAIL logs remain. Final wizard input-theme polish and duplicate-hint removal passed 9 presentation cases and New Dark100, New Light150, Upgrade Classic native recaptures (`native-*-polish.log`). Physical 5K/225%, screen-reader behavior and owner visual approval are not inferred from DPI96 captures or headless text-scale tests.

## GitHub and final qualification boundary

This report accompanies the implementation commit on [Draft PR #3](https://github.com/ProGraMajster/ModernTubeDownloader/pull/3). The exact pushed SHA and current remote CI results are recorded in that PR and in the local artifact `REPORT.md`/`ci-runs.json`, after push; no remote success is inferred here. The baseline CI failed in `Batch.ClipboardRendersCanonicalRowsAndOtherPickerDispatchesOnce` because the hosted tree did not stabilize within its existing pass limit. The strengthened local posted-dispatch/Classic↔Modern sequence passes in the focused presentation run, but that does **not** retroactively turn the old CI run green.

Original captures, semantic snapshots and failed runs remain in the artifact collection. The public-repository audit explicitly allows only the two reviewed preview PNG byte hashes; it does not exempt arbitrary screenshots or personal paths. Raw diagnostic artifacts and the local gallery remain outside Git. Do not merge PR #3.

Intentionally deferred to final 1.3.0 qualification: complete Debug/Release and JavaScript suites, full external Windows UIA, all DPI/resolution combinations, physical 5K/225% native memory, cold startup, 2750/2617 and 10,000-item performance comparisons, long soak, packaged-install/clean-PC/update lifecycle and real-provider authentication/recording. Existing intermittent `NavDownloads`, approximately two-second Invoke, full-root UIA scanning and missing-menu cases remain **DEFERRED / NOT QUALIFIED**. Environment contention is still a hypothesis, not a proven explanation.

No test deletion, weakened assertion, arbitrary timeout increase, repeated CI-until-green, data deletion, main-checkout edit, framework publication, tag or release is part of this report. The remaining work is scoped acceptance and visual refinement of the existing 1.3.0 functionality.
