# Application updates

The optional **Install at manual startup** preference is off for all new and old
profiles unless explicitly selected. It is independent of InstallOnExit. A bounded
foreground preparation runs before queue/LIVE worker admission, respects pending
work, Skip/Later and previous attempts, then uses the existing graceful shutdown and
updater. Background/URL startup does not install. See `STARTUP_UPDATE_POLICY.md` for
the admission and recovery contract and `IMPLEMENTATION_REPORT.md` for test evidence.

`ApplicationUpdateService` checks **ProGraMajster/ModernTubeDownloader GitHub Releases**,
not master. Stable selects the API's latest public non-prerelease, verifies the exact
repository/tag and assets `ModernTubeDownloader-X.Y.Z-win-x64.zip` / `.zip.sha256`.
The ZIP's SHA-256 must match before acceptance and is checked again by the updater.
Transport is HTTPS. Checksums are integrity checks, not a separate publisher signature.

Settings → Application updates: automatic checks (startup, at most daily by default;
daily/weekly), Notify only / Auto-download / Install on exit. Current/latest version,
original release notes, Check/Download/Later/Skip and Install now are localized.
New profiles default to `AutomaticChecks=true`, `StartupDaily`, `InstallOnExit`.
`PolicyVersion=0` identifies unconfirmed/legacy policy; the startup migration examines
the original JSON before deserialization can supply new defaults. Missing legacy mode
and ambiguous old NotifyOnly stay conservative until the migration chooser records a
choice. Recognizable disabled checks, existing modes, skipped versions and deferral are
preserved. Confirmed preferences use `PolicyVersion=1`; unknown update fields round-trip.
Later and skipped version are persisted; they do not permit silent installation.
Both disarm a prepared package. Unchecking that package's Install on exit survives
monitor cycles and restart. Saving policy or explicitly re-enabling its installation is
required to arm it again. Disabling automatic checks or selecting NotifyOnly/AutoDownload
through the policy editor clears an earlier automatic install intent.
Policy saving first durably disarms the prepared package, then saves preferences, and
only then arms installation under the confirmed policy. A failed settings write cannot
leave that package automatically armed.
Metadata checks have a 30-second deadline. Network/timeout failures are logged and
shown as Failed; they do not terminate the recurring monitor or block manual retry.
State and staging live in AppData/ModernTubeDownloader/Updates, never the tool store.
State initialization happens once under the same serialization gate used by update
actions. Every action awaits it, including actions invoked before the background
monitor starts. Restoration emits a state-change event even with automatic checks off,
so an already-created settings panel can show a prepared package immediately.
Unreadable/invalid stored update state blocks network/package mutations and installation;
the original file is retained instead of resetting prior Skip/Defer evidence. Preferences
can still be saved, including disabling checks, without rewriting the blocked update
state. Repairing that state and restarting re-evaluates it; there is no silent reset.
Atomic state writes use unique temporary files and preserve earlier interrupted-write
artifacts.
Restored staging must match the exact official versioned archive path and SHA-256 before
being considered prepared. Repeated download requests reverify and reuse that archive;
they do not download another copy. Automatic checks/download/staging never launch an
installer or request application shutdown. A skipped/deferred release remains suppressed
even if it was already downloaded. Daily throttling is independent of preparing a known
eligible release after the user changes policy.

`ModernTubeDownloader.Updater` has no UI, framework, worker, authentication or registry
dependencies. The GUI copies its files to staging, then launches a request with the
current PID and start-time identity. The updater waits for actual parent exit,
verifies/extracts again, journals each file's backup before replacement and rolls back
on copying failure. One private `.mtd-update-backup` holds the prior managed files.
An interrupted incomplete journal is recovered before the next transaction.
Read-only destination attributes are respected. Recovery skips unchanged destinations
when a denied copy left them intact; one locked changed file does not prevent recovery
of the other files. If any restoration still fails, the journal remains incomplete for
the next recovery attempt and the failure is reported instead of claiming success.
Only validated package files are changed; unlisted installation files are preserved.
No AppData, queues, history, LIVE, cookies, registrations, logs, downloads, certificates
or appearance profiles are updated. Install now uses the normal work warning/shutdown.
Install-on-exit never fires on Hide-to-tray.
Only the normal shutdown path, after Batch/queue/LIVE and durable saves finish, calls the
external installer. `ShouldInstallOnExit` excludes deferral, skipped versions, pending
installers and a previous installation failure. The pending process is tracked by PID
and start time; its result is read on the next application start. Failure or a disappeared
updater without a result is shown as an installation failure and does not arm a retry on
every exit. Manual retry remains available. No elevation is requested.

ZIP validation rejects traversal, absolute/ADS/device/invalid paths, symlinks,
case-insensitive duplicates, unknown root directories and user-data files. File count,
compressed/uncompressed sizes and depth are bounded. Existing reparse points are
rejected. Failed updates leave the old installation intact and record diagnostics.
The optional restart goes through the application's normal single-instance startup.

Validation must include the real updater process against an isolated synthetic
installation. Never use a user's existing installation as an update fixture.
The 1.3.0 candidate still checks public stable releases only; private/beta/candidate
update channels are not supported. Product prerelease and build-metadata suffixes are
handled independently of AssemblyVersion; a candidate can upgrade to the corresponding
stable release, while an installed stable release cannot offer itself again. Portable folder moves require
explicit URI/Native Messaging/autostart registration repair as applicable; the updater
does not silently rewrite registry settings. Clean-PC restart and prolonged staging
retention qualification remain post-1.2 QA.

Additional targeted regressions for corrupted-state preservation, policy-before-monitor
ordering, one-time initialization and the disabled-checks Ready notification were added
after `journey-updates-03`. They are not covered by that earlier 104/104 result; use the
`FullyQualifiedName~ApplicationUpdateTests` filter and record the subsequent run.

Sources: [GitHub Releases API](https://docs.github.com/en/rest/releases/releases)
and [.NET ZIP archive API](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive).
